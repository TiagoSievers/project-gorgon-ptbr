#!/usr/bin/env python3
"""Instalador gráfico Windows — Project Gorgon PT-BR (PyInstaller → INSTALAR.exe)."""
from __future__ import annotations

import sys
import threading
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, scrolledtext, ttk

from windows_core import (
    build_success_message,
    detect_game_dir,
    install_all,
    validate_pack,
)


def _set_app_id() -> None:
    if sys.platform != "win32":
        return
    try:
        import ctypes

        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(
            "ProjectGorgon.PtBr.Installer"
        )
    except OSError:
        pass


def pack_root() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    here = Path(__file__).resolve().parent
    if (here.parent / "dist" / "PgTranslateLive.dll").is_file():
        return here.parent
    if (here / "dist" / "PgTranslateLive.dll").is_file():
        return here
    return here.parent


ROOT = pack_root()


class InstallerApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("Project Gorgon — PT-BR (Windows)")
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
            text="Instalador — tradução PT-BR + BepInEx + plugin",
            font=("Segoe UI", 11, "bold"),
        ).pack(anchor="w", **pad)

        ttk.Label(
            self,
            text="Requisitos: Project Gorgon na Steam (Windows). Tudo já vem neste pacote.",
            wraplength=660,
        ).pack(anchor="w", padx=10)

        path_frame = ttk.Frame(self)
        path_frame.pack(fill="x", **pad)

        ttk.Label(path_frame, text="Pasta do jogo (Steam):").pack(anchor="w")
        row = ttk.Frame(path_frame)
        row.pack(fill="x", pady=4)

        self.game_dir_var = tk.StringVar()
        ttk.Entry(row, textvariable=self.game_dir_var).pack(
            side="left", fill="x", expand=True, padx=(0, 6)
        )
        ttk.Button(row, text="Procurar…", command=self._browse).pack(side="left")
        ttk.Button(row, text="Detectar", command=self._prefill_game_dir).pack(
            side="left", padx=(6, 0)
        )

        btn_row = ttk.Frame(self)
        btn_row.pack(fill="x", **pad)

        self.install_btn = ttk.Button(btn_row, text="Instalar", command=self._run_install)
        self.install_btn.pack(side="left")
        ttk.Button(btn_row, text="Sair", command=self.destroy).pack(side="right")

        self.status_var = tk.StringVar(value="Aguardando…")
        ttk.Label(self, textvariable=self.status_var).pack(anchor="w", padx=10)

        self.progress = ttk.Progressbar(self, mode="determinate", maximum=100)
        self.progress.pack(fill="x", padx=10, pady=(0, 6))

        ttk.Label(self, text="Log:").pack(anchor="w", padx=10)
        self.log = scrolledtext.ScrolledText(self, height=14, state="disabled", wrap="word")
        self.log.pack(fill="both", expand=True, padx=10, pady=(0, 10))

    def _prefill_game_dir(self) -> None:
        detected = detect_game_dir()
        if detected:
            self.game_dir_var.set(str(detected))

    def _browse(self) -> None:
        initial = self.game_dir_var.get().strip() or str(Path.home())
        path = filedialog.askdirectory(
            title="Selecione a pasta Project Gorgon",
            initialdir=initial,
        )
        if path:
            self.game_dir_var.set(path)

    def _append_log(self, text: str) -> None:
        self.log.configure(state="normal")
        self.log.insert("end", text + ("\n" if not text.endswith("\n") else ""))
        self.log.see("end")
        self.log.configure(state="disabled")
        self._last_log += text + "\n"

    def _set_busy(self, busy: bool) -> None:
        self._busy = busy
        self.install_btn.configure(state="disabled" if busy else "normal")

    def _set_progress(self, pct: int, msg: str) -> None:
        self.progress["value"] = pct
        self.status_var.set(msg)

    def _validate(self) -> Path | None:
        missing = validate_pack(ROOT)
        if missing:
            messagebox.showerror(
                "Pacote incompleto",
                "Extraia o .zip inteiro antes de instalar.\n\nFalta:\n"
                + "\n".join(f"• {m}" for m in missing),
            )
            return None
        game = self.game_dir_var.get().strip()
        if not game:
            messagebox.showwarning("Atenção", "Informe ou detecte a pasta do jogo.")
            return None
        game_path = Path(game)
        if not game_path.is_dir():
            messagebox.showerror("Erro", f"Pasta não encontrada:\n{game}")
            return None
        return game_path

    def _run_install(self) -> None:
        if self._busy:
            return
        game_path = self._validate()
        if game_path is None:
            return
        if not messagebox.askyesno(
            "Confirmar",
            "Instalar BepInEx, plugin e language pack PT-BR neste computador?",
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

            result = install_all(ROOT, game_path, progress=on_progress)
            self.after(0, lambda: self._on_done(game_path, result))

        threading.Thread(target=worker, daemon=True).start()

    def _on_done(self, game_path: Path, result) -> None:
        self._set_busy(False)
        for line in result.log:
            self._append_log(line)

        if result.ok:
            messagebox.showinfo(
                "Pronto! Pode abrir o jogo",
                build_success_message(game_path),
            )
            self._show_report()
        else:
            messagebox.showerror(
                "Falha na instalação",
                "Houve erro na instalação.\n\n"
                + "\n".join(result.errors[:8])
                + "\n\nVeja o log para detalhes.",
            )
            self._show_report()

    def _show_report(self) -> None:
        win = tk.Toplevel(self)
        win.title("Relatório técnico")
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
            "Este instalador é para Windows.\n"
            "No Linux use INSTALAR (zenity) na pasta pg-ptbr.",
            file=sys.stderr,
        )
        sys.exit(1)

    missing = validate_pack(ROOT)
    if missing and not getattr(sys, "frozen", False):
        print(f"Erro: pacote incompleto em {ROOT}", file=sys.stderr)
        sys.exit(1)

    app = InstallerApp()
    app.mainloop()


if __name__ == "__main__":
    main()
