#!/usr/bin/env bash
# Prepara Win11-PG-Test: disco, pasta compartilhada, clipboard, ISO Guest Additions (autorun).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VM_NAME="${VM_NAME:-Win11-PG-Test}"
DISK_SIZE_MB="${DISK_SIZE_MB:-122880}"   # 120 GB
SHARED_NAME="${SHARED_NAME:-pgptbr}"
GA_ISO_PORT="${GA_ISO_PORT:-1}"

die() { echo "Erro: $*" >&2; exit 1; }

command -v VBoxManage >/dev/null || die "VirtualBox não encontrado"

vm_state() {
  VBoxManage showvminfo "${VM_NAME}" --machinereadable | sed -n 's/^VMState="\(.*\)"$/\1/p'
}

find_ga_iso() {
  local root="${ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
  local candidates=(
    ${GA_ISO:-}
    "${root}/.cache"/VBoxGuestAdditions*.iso
    /usr/share/virtualbox/VBoxGuestAdditions.iso
    /usr/share/misc/virtualbox/VBoxGuestAdditions.iso
    /opt/VirtualBox/additions/VBoxGuestAdditions.iso
  )
  local p
  for p in ${candidates[@]}; do
    [[ -n "$p" && -f "$p" ]] && { echo "$p"; return 0; }
  done
  return 1
}

ensure_ga_iso() {
  if find_ga_iso >/dev/null 2>&1; then
    return 0
  fi
  echo "Guest Additions ISO não encontrado."
  if command -v apt-get >/dev/null 2>&1 && [[ "${INSTALL_GA_ISO:-}" == "1" ]]; then
    echo "Instalando virtualbox-guest-additions-iso (sudo)…"
    sudo apt-get update -qq
    sudo DEBIAN_FRONTEND=noninteractive apt-get install -y virtualbox-guest-additions-iso
  fi
  if ! find_ga_iso >/dev/null 2>&1; then
    echo "Aviso: instale Guest Additions ISO: sudo apt install virtualbox-guest-additions-iso"
    echo "      Ou defina GA_ISO=/caminho/VBoxGuestAdditions.iso"
    return 1
  fi
}

resize_disk() {
  local vdi cap_mb new_mb
  vdi="$(VBoxManage showvminfo "${VM_NAME}" --machinereadable | sed -n 's/^\"SATA-0-0\"=\"\(.*\)\"$/\1/p')"
  [[ -n "$vdi" && -f "$vdi" ]] || die "VDI não encontrado (SATA-0-0)"

  cap_mb="$(VBoxManage showhdinfo "$vdi" | awk '/Capacity:/ {print $2; exit}')"
  new_mb="$DISK_SIZE_MB"

  if [[ "$cap_mb" -ge "$new_mb" ]]; then
    echo "Disco OK: ${cap_mb} MB (meta ${new_mb} MB)"
    return 0
  fi

  echo "Expandindo disco ${cap_mb} → ${new_mb} MB …"
  VBoxManage modifymedium disk "$vdi" --resize "$new_mb"
  echo "VDI expandido. Na VM: Win+X → Gerenciamento de Discos → Estender C:"
}

setup_shared_folder() {
  if VBoxManage showvminfo "${VM_NAME}" | grep -q "Name: '${SHARED_NAME}'"; then
    echo "Shared folder '${SHARED_NAME}' já configurada."
    return 0
  fi
  VBoxManage sharedfolder add "${VM_NAME}" \
    --name "${SHARED_NAME}" \
    --hostpath "${ROOT}" \
    --automount
  echo "Shared folder: \\\\VBOXSVR\\${SHARED_NAME}  (host: ${ROOT})"
}

configure_vm() {
  VBoxManage modifyvm "${VM_NAME}" \
    --clipboard-mode bidirectional \
    --draganddrop bidirectional \
    --vram 128 \
    --graphicscontroller vboxsvga
  echo "VM: clipboard/drag bidirecional, VRAM 128 MB."
}

mount_guest_additions_iso() {
  local iso
  iso="$(find_ga_iso)"
  echo "Montando Guest Additions (autorun): $iso"
  VBoxManage storageattach "${VM_NAME}" \
    --storagectl SATA \
    --port "${GA_ISO_PORT}" \
    --device 0 \
    --type dvddrive \
    --medium "$iso"
}

prepare_offline() {
  local state
  state="$(vm_state)"
  [[ "$state" == "poweroff" || "$state" == "saved" ]] || \
    die "VM deve estar desligada para prepare (estado: ${state}). Use: VBoxManage controlvm ${VM_NAME} poweroff"

  resize_disk
  setup_shared_folder
  configure_vm
  if ensure_ga_iso; then
    mount_guest_additions_iso
    echo "Guest Additions ISO montado (autorun na VM)."
  else
    echo "Pulando ISO Guest Additions — instale o pacote ISO e rode: make win11-vm-prepare"
  fi
  echo ""
  echo "Pronto. Inicie: make win11-vm"
  echo "Na VM: Estender C: (diskmgmt.msc) se expandiu o disco."
  echo "Release: \\\\VBOXSVR\\${SHARED_NAME}\\releases\\"
}

attach_ga_for_autorun() {
  ensure_ga_iso
  mount_guest_additions_iso
  echo "Guest Additions ISO inserido — na VM, rode VBoxWindowsAdditions.exe se não abrir sozinho."
}

usage() {
  cat <<EOF
Uso: $(basename "$0") [prepare|autorun]

  prepare  — VM desligada: disco ${DISK_SIZE_MB}MB, shared folder, clipboard, ISO GA
  autorun  — VM ligada: reinsere ISO Guest Additions (autorun do instalador)

Variáveis: VM_NAME, DISK_SIZE_MB, SHARED_NAME
EOF
}

main() {
  VBoxManage list vms | grep -q "\"${VM_NAME}\"" || die "VM não encontrada: ${VM_NAME}"

  case "${1:-prepare}" in
    prepare)  prepare_offline ;;
    autorun)
      state="$(vm_state)"
      [[ "$state" == "running" ]] || die "VM deve estar ligada para autorun (estado: ${state})"
      attach_ga_for_autorun
      ;;
    -h|--help) usage ;;
    *) die "Comando desconhecido: $1"; usage; exit 1 ;;
  esac
}

main "$@"
