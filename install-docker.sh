#!/usr/bin/env bash
set -euo pipefail

if (( EUID != 0 )); then
  exec sudo bash "$0" "$@"
fi

source /etc/os-release
if [[ ${ID:-} != ubuntu || -z ${VERSION_CODENAME:-} ]]; then
  printf 'Этот скрипт устанавливает Docker на Ubuntu.\n' >&2
  exit 1
fi

apt-get update
apt-get install -y ca-certificates curl git

if ! command -v docker >/dev/null || ! docker compose version >/dev/null 2>&1; then
  install -d -m 0755 /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc

  if [[ ! -f /etc/apt/sources.list.d/docker.sources && ! -f /etc/apt/sources.list.d/docker.list ]]; then
    printf 'deb [arch=%s signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu %s stable\n' \
      "$(dpkg --print-architecture)" "${UBUNTU_CODENAME:-$VERSION_CODENAME}" >/etc/apt/sources.list.d/docker.list
  fi

  apt-get update
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi

systemctl enable --now docker
docker compose version
printf 'Docker и Git готовы. Переходи к запуску облака.\n'
