#!/usr/bin/env bash
# Fluxo VM Windows: prepare (disco/shared/GA) → descarrega KVM → inicia → autorun GA se pedido.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VM_NAME="${VM_NAME:-Win11-PG-Test}"
PREPARE="${PREPARE:-1}"
GA_AUTORUN="${GA_AUTORUN:-1}"
GA_WAIT_SEC="${GA_WAIT_SEC:-45}"

die() { echo "Erro: $*" >&2; exit 1; }

command -v VBoxManage >/dev/null || die "VirtualBox não encontrado"

if ! VBoxManage list vms | grep -q "\"${VM_NAME}\""; then
  die "VM não encontrada: ${VM_NAME}"
fi

if pgrep -x qemu-system-x86_64 >/dev/null 2>&1; then
  die "Feche VMs QEMU antes de continuar"
fi

unload_kvm() {
  if ! lsmod | grep -q '^kvm'; then
    echo "KVM já está descarregado."
    return 0
  fi
  echo "Descarregando KVM (senha sudo necessária)…"
  if lsmod | grep -q '^kvm_intel'; then
    sudo modprobe -r kvm_intel || die "Falha ao remover kvm_intel — reinicie o PC e tente de novo"
  elif lsmod | grep -q '^kvm_amd'; then
    sudo modprobe -r kvm_amd || die "Falha ao remover kvm_amd — reinicie o PC e tente de novo"
  fi
  sudo modprobe -r kvm || die "Falha ao remover kvm"
  echo "KVM descarregado."
}

vm_state() {
  VBoxManage showvminfo "${VM_NAME}" --machinereadable | sed -n 's/^VMState="\(.*\)"$/\1/p'
}

if [[ "$PREPARE" == "1" ]]; then
  state="$(vm_state)"
  if [[ "$state" == "poweroff" ]]; then
    echo "==> Preparando VM (disco, shared folder, Guest Additions ISO)…"
    bash "${ROOT}/scripts/setup-win11-vm.sh" prepare
  else
    echo "VM em estado '${state}' — pulando prepare (desligue para redimensionar disco)."
  fi
fi

unload_kvm

echo "Iniciando ${VM_NAME}…"
VBoxManage startvm "${VM_NAME}" --type gui

if [[ "$GA_AUTORUN" == "1" ]]; then
  echo "Aguardando ${GA_WAIT_SEC}s para inserir Guest Additions (autorun)…"
  sleep "$GA_WAIT_SEC"
  if [[ "$(vm_state)" == "running" ]]; then
    bash "${ROOT}/scripts/setup-win11-vm.sh" autorun || \
      echo "Aviso: autorun GA falhou — na VM: CD VBoxGuestAdditions → VBoxWindowsAdditions.exe"
  fi
fi

echo ""
echo "Na VM após Guest Additions + estender C:"
echo "  Release: \\\\VBOXSVR\\pgptbr\\releases\\Project-Gorgon-PT-BR-v0.1.0-Windows.zip"
echo "  Ou: https://github.com/TiagoSievers/project-gorgon-ptbr/releases/tag/v0.1.0"
