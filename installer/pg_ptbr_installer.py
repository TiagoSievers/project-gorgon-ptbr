#!/usr/bin/env python3
"""Instalador gráfico Project Gorgon PT-BR — dois cliques (PyInstaller → PgPtBr-Installer)."""
from __future__ import annotations

import os
import queue
import subprocess
import sys
import threading
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, scrolledtext, ttk


def app_root() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent.parent


ROOT = app_root()
INSTALL_SH = ROOT / "scripts" / "install.sh"
VERIFY_SH = ROOT / "scripts" / "verify-install.sh"


def detect_game_dir() -> str:
    paths_sh = ROOT / "scripts" / "install-paths.sh"
    if not paths_sh.is_file():
        return ""
    try:
        out = subprocess.run(
            ["bash", "-c", f'source "{paths_sh}" && printf "%s" "${{GAME_DIR:-}}"'],
            capture_output=True,
            text=True,
            check=False,
            cwd=ROOT,
        )
        return (out.stdout or "").strip()
    except OSError:
        return ""


class InstallerApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("Project Gorgon — PT-BR")
        self.minsize(520, 420)
        self.geometry("640x480")

        self._proc: subprocess.Popen[str] | None = None
        self._log_queue: queue.Queue[str | None] = queue.Queue()

        self._build_ui()
        self._poll_log()
        self._prefill_game_dir()

    def _build_ui(self) -> None:
        pad = {"padx": 10, "pady": 6}

        header = ttk.Label(
            self,
            text="Instalador — tradução PT-BR + BepInEx + plugin",
            font=("", 11, "bold"),
        )
        header.pack(anchor="w", **pad)

        ttk.Label(
            self,
            text="Requisitos: Project Gorgon na Steam (Linux/Proton), internet na 1ª instalação.",
            wraplength=600,
        ).pack(anchor="w", padx=10)

        path_frame = ttk.Frame(self)
        path_frame.pack(fill="x", **pad)

        ttk.Label(path_frame, text="Pasta do jogo:").pack(anchor="w")
        row = ttk.Frame(path_frame)
        row.pack(fill="x", pady=4)

        self.game_dir_var = tk.StringVar()
        self.game_entry = ttk.Entry(row, textvariable=self.game_dir_var)
        self.game_entry.pack(side="left", fill="x", expand=True, padx=(0, 6))

        ttk.Button(row, text="Procurar…", command=self._browse).pack(side="left")
        ttk.Button(row, text="Detectar", command=self._prefill_game_dir).pack(
            side="left", padx=(6, 0)
        )

        btn_row = ttk.Frame(self)
        btn_row.pack(fill="x", **pad)

        self.install_btn = ttk.Button(btn_row, text="Instalar", command=self._run_install)
        self.install_btn.pack(side="left")

        self.verify_btn = ttk.Button(btn_row, text="Verificar", command=self._run_verify)
        self.verify_btn.pack(side="left", padx=8)

        ttk.Button(btn_row, text="Sair", command=self.destroy).pack(side="right")

        self.progress = ttk.Progressbar(self, mode="indeterminate")
        self.progress.pack(fill="x", padx=10, pady=(0, 6))

        ttk.Label(self, text="Log:").pack(anchor="w", padx=10)
        self.log = scrolledtext.ScrolledText(self, height=14, state="disabled", wrap="word")
        self.log.pack(fill="both", expand=True, padx=10, pady=(0, 10))

    def _prefill_game_dir(self) -> None:
        detected = detect_game_dir()
        if detected:
            self.game_dir_var.set(detected)

    def _browse(self) -> None:
        path = filedialog.askdirectory(
            title="Selecione a pasta Project Gorgon",
            initialdir=str(Path.home()),
        )
        if path:
            self.game_dir_var.set(path)

    def _append_log(self, line: str) -> None:
        self.log.configure(state="normal")
        self.log.insert("end", line)
        self.log.see("end")
        self.log.configure(state="disabled")

    def _poll_log(self) -> None:
        while True:
            try:
                line = self._log_queue.get_nowait()
            except queue.Empty:
                break
            if line is None:
                self._on_process_done()
                break
            self._append_log(line)
        self.after(100, self._poll_log)

    def _set_busy(self, busy: bool) -> None:
        state = "disabled" if busy else "normal"
        self.install_btn.configure(state=state)
        self.verify_btn.configure(state=state)
        if busy:
            self.progress.start(12)
        else:
            self.progress.stop()

    def _validate(self) -> bool:
        if not INSTALL_SH.is_file():
            messagebox.showerror(
                "Erro",
                f"install.sh não encontrado em:\n{ROOT}\n\n"
                "Extraia o pacote completo (tar.gz) antes de rodar o instalador.",
            )
            return False
        game = self.game_dir_var.get().strip()
        if not game:
            messagebox.showwarning("Atenção", "Informe ou detecte a pasta do jogo.")
            return False
        if not Path(game).is_dir():
            messagebox.showerror("Erro", f"Pasta não encontrada:\n{game}")
            return False
        return True

    def _run_install(self) -> None:
        if self._proc is not None:
            return
        if not self._validate():
            return
        if not messagebox.askyesno(
            "Confirmar",
            "Instalar BepInEx, plugin e language pack PT-BR neste computador?",
        ):
            return
        self._start_process(["bash", str(INSTALL_SH)], title="Instalando…")

    def _run_verify(self) -> None:
        if self._proc is not None:
            return
        if not VERIFY_SH.is_file():
            messagebox.showerror("Erro", f"verify-install.sh não encontrado em {ROOT}")
            return
        game = self.game_dir_var.get().strip()
        if game and not Path(game).is_dir():
            messagebox.showerror("Erro", f"Pasta não encontrada:\n{game}")
            return
        self._start_process(["bash", str(VERIFY_SH)], title="Verificando…")

    def _start_process(self, cmd: list[str], *, title: str) -> None:
        self._append_log(f"\n--- {title} ---\n")
        self._set_busy(True)

        env = os.environ.copy()
        game = self.game_dir_var.get().strip()
        if game:
            env["GAME_DIR"] = game

        def worker() -> None:
            try:
                self._proc = subprocess.Popen(
                    cmd,
                    cwd=ROOT,
                    env=env,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                )
                assert self._proc.stdout is not None
                for line in self._proc.stdout:
                    self._log_queue.put(line)
                code = self._proc.wait()
                self._log_queue.put(f"\n--- Código de saída: {code} ---\n")
                self._exit_code = code
            except OSError as exc:
                self._log_queue.put(f"\nErro: {exc}\n")
                self._exit_code = 1
            finally:
                self._proc = None
                self._log_queue.put(None)

        self._exit_code = 0
        threading.Thread(target=worker, daemon=True).start()

    def _on_process_done(self) -> None:
        self._set_busy(False)
        if self._exit_code == 0:
            messagebox.showinfo(
                "Instalação finalizada",
                "Tradução PT-BR instalada com sucesso!\n\n"
                "Próximos passos:\n"
                "• Launch Options na Steam (veja o log)\n"
                "• Abra o Project Gorgon na Steam\n\n"
                "Avisos sobre LogOutput.log são normais antes da 1ª execução "
                "ou em logs antigos — ignore se o jogo abrir bem.",
            )
        else:
            messagebox.showerror(
                "Falha",
                "Houve erro na instalação/verificação.\nVeja o log para detalhes.",
            )


def main() -> None:
    if not INSTALL_SH.is_file() and not getattr(sys, "frozen", False):
        print(f"Erro: execute a partir do pacote Release (install.sh em {ROOT})", file=sys.stderr)
        sys.exit(1)
    app = InstallerApp()
    app.mainloop()


if __name__ == "__main__":
    main()
