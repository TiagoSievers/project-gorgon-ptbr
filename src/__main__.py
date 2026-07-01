from __future__ import annotations

import argparse
import sys

from . import fetch_cdn, extract, official_writer, translate
from .serve import run_server


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="python3 -m src",
        description="Pipeline PT-BR Project Gorgon (CDN → extract → translate → write)",
    )
    sub = parser.add_subparsers(dest="command", required=True)

    p_fetch = sub.add_parser("fetch", help="Baixa JSON do CDN + Translation.zip")
    p_fetch.add_argument("--version", help="Versão do jogo (ex: 470)")
    p_fetch.add_argument("--force", action="store_true", help="Re-baixa arquivos existentes")

    p_extract = sub.add_parser("extract", help="Extrai strings EN para cache/strings.json")
    p_extract.add_argument("--version", help="Versão do jogo")

    p_translate = sub.add_parser("translate", help="Traduz via Google (cache/translations.json)")
    p_translate.add_argument(
        "--categories",
        help="Lista separada por vírgula (ex: skills,abilities,ui,items)",
    )
    p_translate.add_argument("--limit", type=int, help="Máximo de strings novas")
    p_translate.add_argument("--workers", type=int, default=8)
    p_translate.add_argument("--delay", type=float, default=0.05, help="Segundos entre requests")
    p_translate.add_argument("--force", action="store_true", help="Re-traduz tudo")

    p_write = sub.add_parser("write", help="Gera output/Translation/ (CDN JSON)")
    p_write.add_argument("--version", help="Versão do jogo (official writer)")

    p_pipe = sub.add_parser("pipeline", help="fetch → extract → translate → write")
    p_pipe.add_argument("--version", help="Versão do jogo")
    p_pipe.add_argument("--workers", type=int, default=8)
    p_pipe.add_argument("--delay", type=float, default=0.05)
    p_pipe.add_argument(
        "--categories",
        help="Só traduz categorias listadas (vírgula)",
    )
    p_pipe.add_argument("--skip-fetch", action="store_true")
    p_pipe.add_argument("--skip-translate", action="store_true")

    p_serve = sub.add_parser("serve", help="Servidor HTTP local para o plugin (127.0.0.1)")
    p_serve.add_argument("--host", default="127.0.0.1")
    p_serve.add_argument("--port", type=int, default=8765)

    args = parser.parse_args(argv)

    if args.command == "fetch":
        fetch_cdn.fetch_cdn(version=args.version, force=args.force)
        return 0

    if args.command == "extract":
        extract.extract(version=args.version)
        return 0

    if args.command == "translate":
        cats = [c.strip() for c in args.categories.split(",")] if args.categories else None
        translate.translate_strings(
            categories=cats,
            limit=args.limit,
            workers=args.workers,
            delay=args.delay,
            force=args.force,
        )
        return 0

    if args.command == "write":
        official_writer.write_official(version=args.version)
        return 0

    if args.command == "pipeline":
        version = args.version
        if not args.skip_fetch:
            version = fetch_cdn.fetch_cdn(version=version)
        extract.extract(version=version)
        if not args.skip_translate:
            cats = [c.strip() for c in args.categories.split(",")] if args.categories else None
            translate.translate_strings(
                categories=cats,
                workers=args.workers,
                delay=args.delay,
            )
        official_writer.write_official(version=version)
        return 0

    if args.command == "serve":
        run_server(host=args.host, port=args.port)
        return 0

    parser.print_help()
    return 1


if __name__ == "__main__":
    sys.exit(main())
