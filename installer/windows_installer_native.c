#include <windows.h>
#include <commctrl.h>
#include <shlobj.h>
#include <stdarg.h>
#include <stdio.h>
#include <wchar.h>

#define APP_TITLE L"Project Gorgon - PT-BR"
#define LOG_CAPACITY 65536

#define IDC_STATUS 1001
#define IDC_PROGRESS 1002
#define IDC_LOG 1003
#define IDC_CLOSE 1004
#define IDC_PATH 1005

#define WM_APP_PROGRESS (WM_APP + 1)
#define WM_APP_LOG (WM_APP + 2)
#define WM_APP_DONE (WM_APP + 3)

typedef struct InstallerUi {
    HWND hwnd;
    HWND path_label;
    HWND status_label;
    HWND progress_bar;
    HWND log_edit;
    HWND close_button;
    HANDLE worker_thread;
    WCHAR root[32768];
    WCHAR game_dir[32768];
    BOOL done;
    BOOL ok;
} InstallerUi;

typedef void (*ProgressFn)(int pct, const WCHAR *msg);

static WCHAR g_log[LOG_CAPACITY];
static size_t g_log_len = 0;
static CRITICAL_SECTION g_log_lock;
static HWND g_progress_hwnd = NULL;

static WCHAR *dup_wstr(const WCHAR *src) {
    size_t len = wcslen(src) + 1;
    WCHAR *dest = HeapAlloc(GetProcessHeap(), 0, len * sizeof(WCHAR));
    if (!dest) {
        return NULL;
    }
    memcpy(dest, src, len * sizeof(WCHAR));
    return dest;
}

static void log_append_v(const WCHAR *fmt, va_list args) {
    EnterCriticalSection(&g_log_lock);
    if (g_log_len < LOG_CAPACITY - 2) {
        va_list copy;
        va_copy(copy, args);
        int written = _vsnwprintf(
            g_log + g_log_len,
            LOG_CAPACITY - g_log_len - 2,
            fmt,
            copy
        );
        va_end(copy);

        if (written >= 0) {
            g_log_len += (size_t)written;
            if (g_log_len < LOG_CAPACITY - 2) {
                g_log[g_log_len++] = L'\r';
                g_log[g_log_len++] = L'\n';
                g_log[g_log_len] = L'\0';
            }
        }
    }
    LeaveCriticalSection(&g_log_lock);

    if (g_progress_hwnd) {
        PostMessageW(g_progress_hwnd, WM_APP_LOG, 0, 0);
    }
}

static void log_append(const WCHAR *fmt, ...) {
    va_list args;
    va_start(args, fmt);
    log_append_v(fmt, args);
    va_end(args);
}

static void log_snapshot(WCHAR *dest, size_t cap) {
    EnterCriticalSection(&g_log_lock);
    _snwprintf(dest, cap, L"%ls", g_log);
    dest[cap - 1] = L'\0';
    LeaveCriticalSection(&g_log_lock);
}

static void join_path(WCHAR *dest, size_t cap, const WCHAR *a, const WCHAR *b) {
    if (!a || !*a) {
        _snwprintf(dest, cap, L"%ls", b);
        dest[cap - 1] = L'\0';
        return;
    }

    size_t len = wcslen(a);
    const WCHAR *sep = (len > 0 && (a[len - 1] == L'\\' || a[len - 1] == L'/')) ? L"" : L"\\";
    _snwprintf(dest, cap, L"%ls%ls%ls", a, sep, b);
    dest[cap - 1] = L'\0';
}

static void parent_dir(WCHAR *path) {
    size_t len = wcslen(path);
    while (len > 0) {
        if (path[len - 1] == L'\\' || path[len - 1] == L'/') {
            path[len - 1] = L'\0';
            return;
        }
        len--;
    }
    path[0] = L'\0';
}

static BOOL file_exists(const WCHAR *path) {
    DWORD attrs = GetFileAttributesW(path);
    return attrs != INVALID_FILE_ATTRIBUTES && !(attrs & FILE_ATTRIBUTE_DIRECTORY);
}

static BOOL dir_exists(const WCHAR *path) {
    DWORD attrs = GetFileAttributesW(path);
    return attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY);
}

static BOOL ensure_dir(const WCHAR *path) {
    if (!path || !*path) {
        return FALSE;
    }
    if (dir_exists(path)) {
        return TRUE;
    }

    WCHAR tmp[32768];
    _snwprintf(tmp, 32768, L"%ls", path);
    tmp[32767] = L'\0';

    for (WCHAR *p = tmp; *p; ++p) {
        if (*p == L'/' || *p == L'\\') {
            WCHAR hold = *p;
            *p = L'\0';
            if (*tmp && !dir_exists(tmp) && !CreateDirectoryW(tmp, NULL)) {
                DWORD err = GetLastError();
                if (err != ERROR_ALREADY_EXISTS) {
                    *p = hold;
                    return FALSE;
                }
            }
            *p = hold;
        }
    }

    if (!dir_exists(tmp) && !CreateDirectoryW(tmp, NULL)) {
        DWORD err = GetLastError();
        if (err != ERROR_ALREADY_EXISTS) {
            return FALSE;
        }
    }
    return TRUE;
}

static BOOL ensure_parent_dir(const WCHAR *path) {
    WCHAR tmp[32768];
    _snwprintf(tmp, 32768, L"%ls", path);
    tmp[32767] = L'\0';
    parent_dir(tmp);
    return ensure_dir(tmp);
}

static BOOL copy_tree(const WCHAR *src, const WCHAR *dest) {
    WCHAR pattern[32768];
    join_path(pattern, 32768, src, L"*");

    if (!ensure_dir(dest)) {
        log_append(L"ERRO: nao foi possivel criar pasta: %ls", dest);
        return FALSE;
    }

    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW(pattern, &fd);
    if (h == INVALID_HANDLE_VALUE) {
        log_append(L"ERRO: pasta nao encontrada: %ls", src);
        return FALSE;
    }

    do {
        if (wcscmp(fd.cFileName, L".") == 0 || wcscmp(fd.cFileName, L"..") == 0) {
            continue;
        }

        WCHAR child_src[32768];
        WCHAR child_dest[32768];
        join_path(child_src, 32768, src, fd.cFileName);
        join_path(child_dest, 32768, dest, fd.cFileName);

        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            if (!copy_tree(child_src, child_dest)) {
                FindClose(h);
                return FALSE;
            }
        } else {
            if (!ensure_parent_dir(child_dest) || !CopyFileW(child_src, child_dest, FALSE)) {
                log_append(L"ERRO: falha ao copiar arquivo: %ls -> %ls", child_src, child_dest);
                FindClose(h);
                return FALSE;
            }
        }
    } while (FindNextFileW(h, &fd));

    FindClose(h);
    return TRUE;
}

static void remove_tree(const WCHAR *path) {
    WCHAR pattern[32768];
    join_path(pattern, 32768, path, L"*");

    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW(pattern, &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do {
            if (wcscmp(fd.cFileName, L".") == 0 || wcscmp(fd.cFileName, L"..") == 0) {
                continue;
            }
            WCHAR child[32768];
            join_path(child, 32768, path, fd.cFileName);
            if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
                remove_tree(child);
                RemoveDirectoryW(child);
            } else {
                SetFileAttributesW(child, FILE_ATTRIBUTE_NORMAL);
                DeleteFileW(child);
            }
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    RemoveDirectoryW(path);
}

static BOOL write_log_file(const WCHAR *root) {
    WCHAR log_path[32768];
    WCHAR snapshot[LOG_CAPACITY];
    join_path(log_path, 32768, root, L"INSTALAR-log.txt");
    log_snapshot(snapshot, LOG_CAPACITY);

    HANDLE h = CreateFileW(
        log_path,
        GENERIC_WRITE,
        FILE_SHARE_READ,
        NULL,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        NULL
    );
    if (h == INVALID_HANDLE_VALUE) {
        return FALSE;
    }

    DWORD bom = 0;
    const BYTE utf16_bom[] = {0xFF, 0xFE};
    WriteFile(h, utf16_bom, sizeof(utf16_bom), &bom, NULL);

    DWORD bytes = 0;
    WriteFile(h, snapshot, (DWORD)(wcslen(snapshot) * sizeof(WCHAR)), &bytes, NULL);
    CloseHandle(h);
    return TRUE;
}

static BOOL powershell_expand_archive(const WCHAR *zip_path, const WCHAR *dest_dir) {
    WCHAR command[32768];
    _snwprintf(
        command,
        32768,
        L"powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden "
        L"-Command \"Expand-Archive -LiteralPath '%ls' -DestinationPath '%ls' -Force\"",
        zip_path,
        dest_dir
    );
    command[32767] = L'\0';

    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    ZeroMemory(&pi, sizeof(pi));
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESHOWWINDOW;
    si.wShowWindow = SW_HIDE;

    if (!CreateProcessW(NULL, command, NULL, NULL, FALSE, 0, NULL, NULL, &si, &pi)) {
        log_append(L"ERRO: nao foi possivel iniciar powershell para extrair o BepInEx");
        return FALSE;
    }

    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD exit_code = 1;
    GetExitCodeProcess(pi.hProcess, &exit_code);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);

    if (exit_code != 0) {
        log_append(L"ERRO: PowerShell falhou ao extrair BepInEx (codigo %lu)", exit_code);
        return FALSE;
    }
    return TRUE;
}

static BOOL read_registry_install_path(HKEY hive, const WCHAR *subkey, WCHAR *out, DWORD out_chars) {
    DWORD size = out_chars * sizeof(WCHAR);
    LONG status = RegGetValueW(
        hive,
        subkey,
        L"InstallPath",
        RRF_RT_REG_SZ,
        NULL,
        out,
        &size
    );
    return status == ERROR_SUCCESS && out[0] != L'\0';
}

static BOOL detect_game_dir(WCHAR *out, DWORD out_chars) {
    const WCHAR *keys[][2] = {
        {(const WCHAR *)HKEY_LOCAL_MACHINE, L"SOFTWARE\\WOW6432Node\\Valve\\Steam"},
        {(const WCHAR *)HKEY_LOCAL_MACHINE, L"SOFTWARE\\Valve\\Steam"},
        {(const WCHAR *)HKEY_CURRENT_USER, L"Software\\Valve\\Steam"},
    };

    WCHAR steam[32768];
    for (int i = 0; i < 3; ++i) {
        HKEY hive = (HKEY)keys[i][0];
        if (read_registry_install_path(hive, keys[i][1], steam, 32768)) {
            WCHAR game[32768];
            join_path(game, 32768, steam, L"steamapps\\common\\Project Gorgon");
            if (dir_exists(game)) {
                _snwprintf(out, out_chars, L"%ls", game);
                out[out_chars - 1] = L'\0';
                return TRUE;
            }
        }
    }

    const WCHAR *defaults[] = {
        L"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Project Gorgon",
        L"C:\\Program Files\\Steam\\steamapps\\common\\Project Gorgon",
    };
    for (int i = 0; i < 2; ++i) {
        if (dir_exists(defaults[i])) {
            _snwprintf(out, out_chars, L"%ls", defaults[i]);
            out[out_chars - 1] = L'\0';
            return TRUE;
        }
    }
    return FALSE;
}

static BOOL browse_for_folder(HWND owner, WCHAR *out, DWORD out_chars) {
    BROWSEINFOW bi;
    ZeroMemory(&bi, sizeof(bi));
    bi.hwndOwner = owner;
    bi.lpszTitle = L"Selecione a pasta do Project Gorgon";
    bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;

    PIDLIST_ABSOLUTE pidl = SHBrowseForFolderW(&bi);
    if (!pidl) {
        return FALSE;
    }

    BOOL ok = SHGetPathFromIDListW(pidl, out);
    CoTaskMemFree(pidl);
    if (!ok) {
        return FALSE;
    }
    out[out_chars - 1] = L'\0';
    return TRUE;
}

static BOOL validate_pack(const WCHAR *root, WCHAR *missing, DWORD missing_chars) {
    WCHAR path[32768];
    missing[0] = L'\0';

    join_path(path, 32768, root, L"dist\\PgTranslateLive.dll");
    if (!file_exists(path)) {
        _snwprintf(missing, missing_chars, L"dist\\PgTranslateLive.dll");
        return FALSE;
    }

    join_path(path, 32768, root, L"dist\\Translator.dll");
    if (!file_exists(path)) {
        _snwprintf(missing, missing_chars, L"dist\\Translator.dll");
        return FALSE;
    }

    join_path(path, 32768, root, L"output\\Translation\\version.json");
    if (!file_exists(path)) {
        _snwprintf(missing, missing_chars, L"output\\Translation\\version.json");
        return FALSE;
    }

    join_path(path, 32768, root, L"output\\pt-BR\\ui.yaml");
    if (!file_exists(path)) {
        _snwprintf(missing, missing_chars, L"output\\pt-BR\\ui.yaml");
        return FALSE;
    }

    join_path(path, 32768, root, L"vendor\\BepInExPack_IL2CPP.zip");
    if (!file_exists(path)) {
        _snwprintf(missing, missing_chars, L"vendor\\BepInExPack_IL2CPP.zip");
        return FALSE;
    }

    return TRUE;
}

static BOOL install_bepinex(const WCHAR *root, const WCHAR *game_dir) {
    WCHAR winhttp[32768];
    WCHAR core[32768];
    join_path(winhttp, 32768, game_dir, L"winhttp.dll");
    join_path(core, 32768, game_dir, L"BepInEx\\core\\BepInEx.Unity.IL2CPP.dll");

    if (file_exists(winhttp) && file_exists(core)) {
        log_append(L"BepInEx ja instalado");
        return TRUE;
    }

    WCHAR zip_path[32768];
    WCHAR temp_root[32768];
    WCHAR staging[32768];
    WCHAR source_dir[32768];
    WCHAR maybe_pack[32768];

    join_path(zip_path, 32768, root, L"vendor\\BepInExPack_IL2CPP.zip");
    GetTempPathW(32768, temp_root);
    join_path(staging, 32768, temp_root, L"pg-ptbr-bepinex-pack");

    if (dir_exists(staging)) {
        remove_tree(staging);
    }
    if (!ensure_dir(staging)) {
        log_append(L"ERRO: nao foi possivel criar pasta temporaria: %ls", staging);
        return FALSE;
    }

    log_append(L"Extraindo BepInEx...");
    if (!powershell_expand_archive(zip_path, staging)) {
        return FALSE;
    }

    join_path(maybe_pack, 32768, staging, L"BepInExPack");
    if (dir_exists(maybe_pack)) {
        _snwprintf(source_dir, 32768, L"%ls", maybe_pack);
    } else {
        _snwprintf(source_dir, 32768, L"%ls", staging);
    }
    source_dir[32767] = L'\0';

    if (!copy_tree(source_dir, game_dir)) {
        return FALSE;
    }

    if (!file_exists(winhttp)) {
        log_append(L"ERRO: winhttp.dll nao encontrado apos instalar BepInEx");
        return FALSE;
    }

    log_append(L"BepInEx instalado");
    return TRUE;
}

static BOOL install_plugin(const WCHAR *root, const WCHAR *game_dir) {
    WCHAR dll_src[32768];
    WCHAR dll_dest[32768];
    WCHAR cfg_src[32768];
    WCHAR cfg_dest[32768];

    join_path(dll_src, 32768, root, L"dist\\PgTranslateLive.dll");
    join_path(dll_dest, 32768, game_dir, L"BepInEx\\plugins\\PgTranslateLive\\PgTranslateLive.dll");
    if (!ensure_parent_dir(dll_dest) || !CopyFileW(dll_src, dll_dest, FALSE)) {
        log_append(L"ERRO: falha ao copiar PgTranslateLive.dll");
        return FALSE;
    }

    join_path(cfg_src, 32768, root, L"dist\\com.pg.translatelive.cfg");
    if (file_exists(cfg_src)) {
        join_path(cfg_dest, 32768, game_dir, L"BepInEx\\config\\com.pg.translatelive.cfg");
        if (!ensure_parent_dir(cfg_dest) || !CopyFileW(cfg_src, cfg_dest, FALSE)) {
            log_append(L"ERRO: falha ao copiar com.pg.translatelive.cfg");
            return FALSE;
        }
    }

    log_append(L"Plugin instalado");
    return TRUE;
}

static BOOL install_translator(const WCHAR *root, const WCHAR *game_dir) {
    WCHAR dll_src[32768];
    WCHAR dll_dest[32768];
    WCHAR yaml_src[32768];
    WCHAR yaml_dest[32768];
    WCHAR cfg_src[32768];
    WCHAR cfg_dest[32768];

    join_path(dll_src, 32768, root, L"dist\\Translator.dll");
    join_path(dll_dest, 32768, game_dir, L"BepInEx\\plugins\\Translator\\Translator.dll");
    if (!ensure_parent_dir(dll_dest) || !CopyFileW(dll_src, dll_dest, FALSE)) {
        log_append(L"ERRO: falha ao copiar Translator.dll");
        return FALSE;
    }

    join_path(yaml_src, 32768, root, L"output\\pt-BR");
    join_path(yaml_dest, 32768, game_dir, L"BepInEx\\plugins\\Translator\\translations\\pt-BR");
    if (!copy_tree(yaml_src, yaml_dest)) {
        log_append(L"ERRO: falha ao copiar output/pt-BR");
        return FALSE;
    }

    join_path(cfg_src, 32768, root, L"dist\\com.pickteam.translator.cfg");
    if (file_exists(cfg_src)) {
        join_path(cfg_dest, 32768, game_dir, L"BepInEx\\config\\com.pickteam.translator.cfg");
        if (!ensure_parent_dir(cfg_dest) || !CopyFileW(cfg_src, cfg_dest, FALSE)) {
            log_append(L"ERRO: falha ao copiar com.pickteam.translator.cfg");
            return FALSE;
        }
    }

    log_append(L"Translator + YAML pt-BR instalados");
    return TRUE;
}

static BOOL install_translation(const WCHAR *root) {
    WCHAR src[32768];
    WCHAR profile[32768];
    WCHAR dest[32768];

    join_path(src, 32768, root, L"output\\Translation");
    if (!GetEnvironmentVariableW(L"USERPROFILE", profile, 32768)) {
        log_append(L"ERRO: USERPROFILE nao encontrado");
        return FALSE;
    }

    join_path(dest, 32768, profile, L"AppData\\LocalLow\\Elder Game\\Project Gorgon\\Translation");
    if (!copy_tree(src, dest)) {
        log_append(L"ERRO: falha ao copiar language pack");
        return FALSE;
    }

    log_append(L"Language pack instalado");
    return TRUE;
}

static BOOL verify_install(const WCHAR *game_dir) {
    WCHAR check[32768];
    WCHAR profile[32768];

    join_path(check, 32768, game_dir, L"winhttp.dll");
    if (!file_exists(check)) {
        log_append(L"ERRO: winhttp.dll ausente apos instalacao");
        return FALSE;
    }

    join_path(check, 32768, game_dir, L"dotnet\\coreclr.dll");
    if (!file_exists(check)) {
        log_append(L"ERRO: dotnet\\coreclr.dll ausente apos instalacao");
        return FALSE;
    }

    join_path(check, 32768, game_dir, L"BepInEx\\core\\BepInEx.Unity.IL2CPP.dll");
    if (!file_exists(check)) {
        log_append(L"ERRO: BepInEx.Unity.IL2CPP.dll ausente apos instalacao");
        return FALSE;
    }

    join_path(check, 32768, game_dir, L"BepInEx\\plugins\\PgTranslateLive\\PgTranslateLive.dll");
    if (!file_exists(check)) {
        log_append(L"ERRO: PgTranslateLive.dll ausente apos instalacao");
        return FALSE;
    }

    join_path(check, 32768, game_dir, L"BepInEx\\plugins\\Translator\\Translator.dll");
    if (!file_exists(check)) {
        log_append(L"ERRO: Translator.dll ausente apos instalacao");
        return FALSE;
    }

    join_path(check, 32768, game_dir, L"BepInEx\\plugins\\Translator\\translations\\pt-BR\\ui.yaml");
    if (!file_exists(check)) {
        log_append(L"ERRO: ui.yaml ausente em translations/pt-BR");
        return FALSE;
    }

    if (!GetEnvironmentVariableW(L"USERPROFILE", profile, 32768)) {
        log_append(L"ERRO: USERPROFILE nao encontrado");
        return FALSE;
    }

    join_path(check, 32768, profile, L"AppData\\LocalLow\\Elder Game\\Project Gorgon\\Translation\\version.json");
    if (!file_exists(check)) {
        log_append(L"ERRO: version.json ausente apos instalacao");
        return FALSE;
    }

    log_append(L"Verificacao final: OK");
    return TRUE;
}

static void noop_progress(int pct, const WCHAR *msg) {
    (void)pct;
    (void)msg;
}

static void ui_progress(int pct, const WCHAR *msg) {
    if (!g_progress_hwnd) {
        return;
    }
    WCHAR *copy = dup_wstr(msg);
    if (!copy) {
        return;
    }
    PostMessageW(g_progress_hwnd, WM_APP_PROGRESS, (WPARAM)pct, (LPARAM)copy);
}

static BOOL install_all(const WCHAR *root, const WCHAR *game_dir, ProgressFn progress) {
    ProgressFn prog = progress ? progress : noop_progress;

    prog(5, L"Iniciando instalacao...");
    log_append(L"Pacote: %ls", root);
    log_append(L"Jogo: %ls", game_dir);

    prog(20, L"Instalando BepInEx...");
    if (!install_bepinex(root, game_dir)) {
        return FALSE;
    }

    prog(45, L"Instalando plugin PgTranslateLive...");
    if (!install_plugin(root, game_dir)) {
        return FALSE;
    }

    prog(60, L"Instalando Translator + YAML pt-BR...");
    if (!install_translator(root, game_dir)) {
        return FALSE;
    }

    prog(75, L"Copiando language pack PT-BR...");
    if (!install_translation(root)) {
        return FALSE;
    }

    prog(92, L"Verificando instalacao...");
    if (!verify_install(game_dir)) {
        return FALSE;
    }

    prog(100, L"Instalacao concluida!");
    log_append(L">>> Pronto! Abra o jogo na Steam.");
    return TRUE;
}

static void ui_refresh_log(InstallerUi *ui) {
    WCHAR snapshot[LOG_CAPACITY];
    log_snapshot(snapshot, LOG_CAPACITY);
    SetWindowTextW(ui->log_edit, snapshot);
    int len = GetWindowTextLengthW(ui->log_edit);
    SendMessageW(ui->log_edit, EM_SETSEL, (WPARAM)len, (LPARAM)len);
    SendMessageW(ui->log_edit, EM_SCROLLCARET, 0, 0);
}

static DWORD WINAPI install_worker_thread(LPVOID param) {
    InstallerUi *ui = (InstallerUi *)param;
    ui->ok = install_all(ui->root, ui->game_dir, ui_progress);
    write_log_file(ui->root);
    PostMessageW(ui->hwnd, WM_APP_DONE, (WPARAM)ui->ok, 0);
    return 0;
}

static void ui_create_controls(InstallerUi *ui) {
    HFONT font = (HFONT)GetStockObject(DEFAULT_GUI_FONT);
    HWND hwnd = ui->hwnd;

    HWND intro = CreateWindowW(
        L"STATIC",
        L"Instalando BepInEx, plugin e language pack PT-BR...",
        WS_CHILD | WS_VISIBLE,
        12, 12, 680, 22,
        hwnd, NULL, NULL, NULL
    );
    SendMessageW(intro, WM_SETFONT, (WPARAM)font, TRUE);

    ui->path_label = CreateWindowW(
        L"STATIC",
        ui->game_dir,
        WS_CHILD | WS_VISIBLE,
        12, 38, 680, 34,
        hwnd, (HMENU)IDC_PATH, NULL, NULL
    );
    SendMessageW(ui->path_label, WM_SETFONT, (WPARAM)font, TRUE);

    ui->status_label = CreateWindowW(
        L"STATIC",
        L"Preparando...",
        WS_CHILD | WS_VISIBLE,
        12, 78, 680, 22,
        hwnd, (HMENU)IDC_STATUS, NULL, NULL
    );
    SendMessageW(ui->status_label, WM_SETFONT, (WPARAM)font, TRUE);

    ui->progress_bar = CreateWindowExW(
        0,
        PROGRESS_CLASSW,
        NULL,
        WS_CHILD | WS_VISIBLE,
        12, 104, 680, 24,
        hwnd, (HMENU)IDC_PROGRESS, NULL, NULL
    );
    SendMessageW(ui->progress_bar, PBM_SETRANGE, 0, MAKELPARAM(0, 100));
    SendMessageW(ui->progress_bar, PBM_SETPOS, 0, 0);

    ui->log_edit = CreateWindowExW(
        WS_EX_CLIENTEDGE,
        L"EDIT",
        L"",
        WS_CHILD | WS_VISIBLE | WS_VSCROLL | ES_MULTILINE | ES_AUTOVSCROLL | ES_READONLY,
        12, 140, 680, 290,
        hwnd, (HMENU)IDC_LOG, NULL, NULL
    );
    SendMessageW(ui->log_edit, WM_SETFONT, (WPARAM)font, TRUE);

    ui->close_button = CreateWindowW(
        L"BUTTON",
        L"Fechar",
        WS_CHILD | WS_VISIBLE | WS_TABSTOP,
        602, 440, 90, 28,
        hwnd, (HMENU)IDC_CLOSE, NULL, NULL
    );
    SendMessageW(ui->close_button, WM_SETFONT, (WPARAM)font, TRUE);
    EnableWindow(ui->close_button, FALSE);
}

static LRESULT CALLBACK installer_wndproc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    InstallerUi *ui = (InstallerUi *)GetWindowLongPtrW(hwnd, GWLP_USERDATA);

    switch (msg) {
        case WM_NCCREATE: {
            CREATESTRUCTW *cs = (CREATESTRUCTW *)lParam;
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, (LONG_PTR)cs->lpCreateParams);
            return TRUE;
        }
        case WM_CREATE:
            ui = (InstallerUi *)((CREATESTRUCTW *)lParam)->lpCreateParams;
            ui->hwnd = hwnd;
            g_progress_hwnd = hwnd;
            ui_create_controls(ui);
            ui_refresh_log(ui);
            return 0;
        case WM_APP_PROGRESS: {
            WCHAR *text = (WCHAR *)lParam;
            SendMessageW(ui->progress_bar, PBM_SETPOS, wParam, 0);
            SetWindowTextW(ui->status_label, text ? text : L"Instalando...");
            ui_refresh_log(ui);
            if (text) {
                HeapFree(GetProcessHeap(), 0, text);
            }
            return 0;
        }
        case WM_APP_LOG:
            ui_refresh_log(ui);
            return 0;
        case WM_APP_DONE:
            ui->done = TRUE;
            EnableWindow(ui->close_button, TRUE);
            SetFocus(ui->close_button);
            ui_refresh_log(ui);
            if ((BOOL)wParam) {
                SendMessageW(ui->progress_bar, PBM_SETPOS, 100, 0);
                SetWindowTextW(ui->status_label, L"Instalacao concluida!");
                SetWindowTextW(hwnd, APP_TITLE L" - Concluido");
                MessageBoxW(
                    hwnd,
                    L"Instalacao concluida!\n\nAbra o Project Gorgon pela Steam.\n"
                    L"Se perguntar, aceite o language pack PT-BR.\n\n"
                    L"O relatorio tecnico foi salvo em INSTALAR-log.txt.",
                    APP_TITLE,
                    MB_OK | MB_ICONINFORMATION
                );
            } else {
                SetWindowTextW(ui->status_label, L"Falha na instalacao.");
                SetWindowTextW(hwnd, APP_TITLE L" - Falha");
                MessageBoxW(
                    hwnd,
                    L"Houve erro na instalacao.\n\nVeja o log nesta janela ou o arquivo "
                    L"INSTALAR-log.txt na pasta do pacote.",
                    APP_TITLE,
                    MB_OK | MB_ICONERROR
                );
            }
            return 0;
        case WM_COMMAND:
            if (LOWORD(wParam) == IDC_CLOSE) {
                if (!ui->done) {
                    MessageBoxW(hwnd, L"Aguarde a instalacao terminar.", APP_TITLE, MB_OK | MB_ICONINFORMATION);
                    return 0;
                }
                DestroyWindow(hwnd);
                return 0;
            }
            break;
        case WM_CLOSE:
            if (ui && !ui->done) {
                MessageBoxW(hwnd, L"Aguarde a instalacao terminar.", APP_TITLE, MB_OK | MB_ICONINFORMATION);
                return 0;
            }
            DestroyWindow(hwnd);
            return 0;
        case WM_DESTROY:
            g_progress_hwnd = NULL;
            if (ui && ui->worker_thread) {
                CloseHandle(ui->worker_thread);
                ui->worker_thread = NULL;
            }
            PostQuitMessage(0);
            return 0;
        default:
            break;
    }

    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

static BOOL run_progress_window(HINSTANCE instance, const WCHAR *root, const WCHAR *game_dir) {
    InstallerUi ui;
    ZeroMemory(&ui, sizeof(ui));
    _snwprintf(ui.root, 32768, L"%ls", root);
    _snwprintf(ui.game_dir, 32768, L"%ls", game_dir);
    ui.root[32767] = L'\0';
    ui.game_dir[32767] = L'\0';

    const WCHAR *class_name = L"PgPtBrInstallerWindow";
    WNDCLASSW wc;
    ZeroMemory(&wc, sizeof(wc));
    wc.lpfnWndProc = installer_wndproc;
    wc.hInstance = instance;
    wc.hCursor = LoadCursorW(NULL, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wc.lpszClassName = class_name;

    if (!RegisterClassW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
        MessageBoxW(NULL, L"Nao foi possivel criar a janela de instalacao.", APP_TITLE, MB_OK | MB_ICONERROR);
        return FALSE;
    }

    ui.hwnd = CreateWindowExW(
        0,
        class_name,
        APP_TITLE L" - Instalando",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
        CW_USEDEFAULT, CW_USEDEFAULT, 720, 520,
        NULL, NULL, instance, &ui
    );
    if (!ui.hwnd) {
        MessageBoxW(NULL, L"Nao foi possivel abrir a janela de instalacao.", APP_TITLE, MB_OK | MB_ICONERROR);
        return FALSE;
    }

    ShowWindow(ui.hwnd, SW_SHOW);
    UpdateWindow(ui.hwnd);

    ui.worker_thread = CreateThread(NULL, 0, install_worker_thread, &ui, 0, NULL);
    if (!ui.worker_thread) {
        DestroyWindow(ui.hwnd);
        MessageBoxW(NULL, L"Nao foi possivel iniciar a instalacao.", APP_TITLE, MB_OK | MB_ICONERROR);
        return FALSE;
    }

    MSG message;
    while (GetMessageW(&message, NULL, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }

    return ui.ok;
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE prev, PWSTR cmd, int show) {
    (void)prev;
    (void)cmd;
    (void)show;

    INITCOMMONCONTROLSEX icc;
    icc.dwSize = sizeof(icc);
    icc.dwICC = ICC_PROGRESS_CLASS;
    InitCommonControlsEx(&icc);

    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    InitializeCriticalSection(&g_log_lock);

    WCHAR exe_path[32768];
    WCHAR root[32768];
    WCHAR missing[512];
    WCHAR game_dir[32768];
    WCHAR msg[4096];
    int exit_code = 0;

    g_log[0] = L'\0';
    g_log_len = 0;

    if (!GetModuleFileNameW(NULL, exe_path, 32768)) {
        MessageBoxW(NULL, L"Nao foi possivel localizar o executavel.", APP_TITLE, MB_ICONERROR);
        exit_code = 1;
        goto cleanup;
    }

    _snwprintf(root, 32768, L"%ls", exe_path);
    root[32767] = L'\0';
    parent_dir(root);

    if (!validate_pack(root, missing, 512)) {
        _snwprintf(
            msg,
            4096,
            L"Pacote incompleto. Extraia o .zip inteiro antes de instalar.\n\nFalta:\n%ls",
            missing
        );
        msg[4095] = L'\0';
        MessageBoxW(NULL, msg, APP_TITLE, MB_ICONERROR | MB_OK);
        exit_code = 1;
        goto cleanup;
    }

    game_dir[0] = L'\0';
    if (detect_game_dir(game_dir, 32768)) {
        _snwprintf(
            msg,
            4096,
            L"Pasta do jogo detectada:\n\n%ls\n\nDeseja usar essa pasta?",
            game_dir
        );
        msg[4095] = L'\0';
        int answer = MessageBoxW(NULL, msg, APP_TITLE, MB_YESNOCANCEL | MB_ICONQUESTION);
        if (answer == IDCANCEL) {
            goto cleanup;
        }
        if (answer == IDNO) {
            game_dir[0] = L'\0';
        }
    }

    if (!game_dir[0]) {
        if (!browse_for_folder(NULL, game_dir, 32768)) {
            MessageBoxW(NULL, L"Instalacao cancelada.", APP_TITLE, MB_OK | MB_ICONINFORMATION);
            goto cleanup;
        }
    }

    if (!dir_exists(game_dir)) {
        MessageBoxW(NULL, L"A pasta selecionada nao existe.", APP_TITLE, MB_OK | MB_ICONERROR);
        exit_code = 1;
        goto cleanup;
    }

    _snwprintf(
        msg,
        4096,
        L"O instalador vai copiar BepInEx, plugin e language pack PT-BR para:\n\n%ls\n\n"
        L"Depois disso, uma janela vai mostrar o progresso da instalacao.",
        game_dir
    );
    msg[4095] = L'\0';
    if (MessageBoxW(NULL, msg, APP_TITLE, MB_OKCANCEL | MB_ICONQUESTION) != IDOK) {
        goto cleanup;
    }

    if (!run_progress_window(instance, root, game_dir)) {
        exit_code = 1;
    }

cleanup:
    DeleteCriticalSection(&g_log_lock);
    CoUninitialize();
    return exit_code;
}
