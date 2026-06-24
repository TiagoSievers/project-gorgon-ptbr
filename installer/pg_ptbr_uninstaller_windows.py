#!/usr/bin/env python3
"""Desinstalador gráfico Windows — Project Gorgon PT-BR."""
from __future__ import annotations

import sys
import threading
import tkinter as tk
from pathlib import Path
from tkinter import messagebox, scrolledtext, ttk

from windows_core import (
    UNINSTALLER_WIN_EXE,
    build_uninstall_message,
    detect_game_dir,
    schedule_self_delete,
    uninstall_all,
)


def _set_app_id() -> None:
    if sys.platform != "win32":
        return
    try:
        import ctypes

        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(
            "ProjectGorgon.PtBr.Uninstaller"
        )
    except OSError:
        pass


def game_dir_from_install_location() -> Path | None:
    if getattr(sys, "frozen", False):
        game = Path(sys.executable).resolve().parent
        if game.is_dir():
            return game
    return detect_game_dir()


class UninstallerApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("Project Gorgon — PT-BR (Windows) — Desinstalar")
        self.minsize(560, 480)
        self.geometry("700x520")

        self._busy = False
        self._last_log = ""

        self._build_ui()
        self._prefill_game_dir()

    def _build_ui(self) -> None:
        pad = {"padx": 10, "pady": 6}

        ttk.Label(
            self,
            text="Desinstalador — remover tradução PT-BR + BepInEx + plugins",
            font=("Segoe UI", 11, "bold"),
        ).pack(anchor="w", **pad)

        ttk.Label(
            self,
            text=(
                "Remove BepInEx, plugins PgTranslateLive/Translator, winhttp.dll "
                "e o language pack PT-BR. O jogo volta ao inglês original."
            ),
            wraplength=660,
        ).pack(anchor="w", padx=10)

        path_frame = ttk.Frame(self)
        path_frame.pack(fill="x", **pad)

        ttk.Label(path_frame, text="Pasta do jogo (Steam):").pack(anchor="w")
        row = ttk.Frame(path_frame)
        row.pack(fill="x", pady=4)

        self.game_dir_var = tk.StringVar()
        ttk.Entry(row, textvariable=self.game_dir_var, state="readonly").pack(
            side="left", fill="x", expand=True, padx=(0, 6)
        )
        ttk.Button(row, text="Detectar", command=self._prefill_game_dir).pack(side="left")

        btn_row = ttk.Frame(self)
        btn_row.pack(fill="x", **pad)

        self.uninstall_btn = ttk.Button(
            btn_row, text="Desinstalar", command=self._run_uninstall
        )
        self.uninstall_btn.pack(side="left")
        ttk.Button(btn_row, text="Sair", command=self.destroy).pack(side="right")

        self.status_var = tk.StringVar(value="Aguardando…")
        ttk.Label(self, textvariable=self.status_var).pack(anchor="w", padx=10)

        self.progress = ttk.Progressbar(self, mode="determinate", maximum=100)
        self.progress.pack(fill="x", padx=10, pady=(0, 6))

        ttk.Label(self, text="Log:").pack(anchor="w", padx=10)
        self.log = scrolledtext.ScrolledText(self, height=14, state="disabled", wrap="word")
        self.log.pack(fill="both", expand=True, padx=10, pady=(0, 10))

    def _prefill_game_dir(self) -> None:
        detected = game_dir_from_install_location()
        if detected:
            self.game_dir_var.set(str(detected))

    def _append_log(self, text: str) -> None:
        self.log.configure(state="normal")
        self.log.insert("end", text + ("\n" if not text.endswith("\n") else ""))
        self.log.see("end")
        self.log.configure(state="disabled")
        self._last_log += text + "\n"

    def _set_busy(self, busy: bool) -> None:
        self._busy = busy
        self.uninstall_btn.configure(state="disabled" if busy else "normal")

    def _set_progress(self, pct: int, msg: str) -> None:
        self.progress["value"] = pct
        self.status_var.set(msg)

    def _validate(self) -> Path | None:
        game = self.game_dir_var.get().strip()
        if not game:
            messagebox.showwarning(
                "Atenção",
                "Não foi possível detectar a pasta do jogo.\n\n"
                f"Execute {UNINSTALLER_WIN_EXE} de dentro de:\n"
                "…\\Project Gorgon\\",
            )
            return None
        game_path = Path(game)
        if not game_path.is_dir():
            messagebox.showerror("Erro", f"Pasta não encontrada:\n{game}")
            return None
        return game_path

    def _run_uninstall(self) -> None:
        if self._busy:
            return
        game_path = self._validate()
        if game_path is None:
            return
        if not messagebox.askyesno(
            "Confirmar desinstalação",
            "Remover tradução PT-BR deste computador?\n\n"
            "Serão apagados:\n"
            "• BepInEx, dotnet, winhttp.dll (pasta do jogo)\n"
            "• Plugins PgTranslateLive e Translator\n"
            "• Language pack em AppData\n\n"
            "O jogo voltará ao inglês. Continuar?",
        ):
            return

        self._last_log = ""
        self.log.configure(state="normal")
        self.log.delete("1.0", "end")
        self.log.configure(state="disabled")
        self._set_busy(True)
        self._set_progress(0, "Iniciando…")

        def worker() -> None:
            def on_progress(pct: int, msg: str) -> None:
                self.after(0, lambda: self._set_progress(pct, msg))

            result = uninstall_all(game_path, progress=on_progress)
            self.after(0, lambda: self._on_done(result))

        threading.Thread(target=worker, daemon=True).start()

    def _on_done(self, result) -> None:
        self._set_busy(False)
        for line in result.log:
            self._append_log(line)

        if result.ok:
            if getattr(sys, "frozen", False):
                schedule_self_delete(Path(sys.executable))
            messagebox.showinfo(
                "Desinstalação concluída",
                build_uninstall_message(result),
            )
            self._show_report()
        else:
            messagebox.showerror(
                "Falha na desinstalação",
                "Houve erro ao remover o mod.\n\n"
                + "\n".join(result.errors[:8])
                + "\n\nVeja o log para detalhes.",
            )
            self._show_report()

    def _show_report(self) -> None:
        win = tk.Toplevel(self)
        win.title("Relatório técnico — desinstalação")
        win.geometry("720x480")
        win.minsize(520, 320)

        text = scrolledtext.ScrolledText(win, wrap="word")
        text.pack(fill="both", expand=True, padx=8, pady=8)
        text.insert("1.0", self._last_log or "(sem log)")
        text.configure(state="disabled")

        ttk.Button(win, text="Fechar", command=win.destroy).pack(pady=(0, 8))


def main() -> None:
    _set_app_id()
    if sys.platform != "win32":
        print(
            "Este desinstalador é para Windows.\n"
            "No Linux use uninstall-language-pack-ptbr na pasta do jogo.",
            file=sys.stderr,
        )
        sys.exit(1)

    app = UninstallerApp()
    app.mainloop()


if __name__ == "__main__":
    main()
