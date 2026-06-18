#!/usr/bin/env bash
# Descarrega KVM (conflita com VirtualBox) e inicia Win11-PG-Test.
set -euo pipefail

VM_NAME="${VM_NAME:-Win11-PG-Test}"

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

unload_kvm

echo "Iniciando ${VM_NAME}…"
VBoxManage startvm "${VM_NAME}" --type gui
