#!/usr/bin/env bash
set -euo pipefail

cd -- "$(dirname -- "${BASH_SOURCE[0]}")"

if [[ ${EUID} -eq 0 ]]; then
  docker=(docker)
else
  docker=(sudo docker)
fi

ip=${1:-}
if [[ -z "$ip" ]]; then
  read -r -p 'Публичный IPv4 из панели VPS: ' ip
fi

if ! [[ "$ip" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]]; then
  printf 'Укажи публичный IPv4 без https:// и порта.\n' >&2
  exit 1
fi
IFS=. read -r a b c d <<< "$ip"
for octet in "$a" "$b" "$c" "$d"; do
  if (( 10#$octet > 255 )) || [[ "$octet" != "$((10#$octet))" ]]; then
    printf 'Некорректный IPv4.\n' >&2
    exit 1
  fi
done

if (( a == 0 || a == 10 || a == 127 || a >= 224 ||
      (a == 100 && b >= 64 && b <= 127) || (a == 169 && b == 254) ||
      (a == 172 && b >= 16 && b <= 31) || (a == 192 && b == 168) )); then
  printf 'Для HTTPS нужен публичный IPv4 VPS.\n' >&2
  exit 1
fi

"${docker[@]}" compose version >/dev/null
env_file=$(mktemp .env.XXXXXX)
trap 'rm -f -- "$env_file"' EXIT
printf 'CLOUD_IP=%s\n' "$ip" > "$env_file"
mv -- "$env_file" .env

compose=("${docker[@]}" compose)
address="https://$ip"

printf 'Собираем OblakoHub Server…\n'
"${compose[@]}" build cloud
"${compose[@]}" up -d --force-recreate caddy

printf 'Ждём доверенный HTTPS от Let’s Encrypt…\n'
ready=false
for ((i=0; i<90; i++)); do
  if [[ "$(curl --noproxy '*' -fsS --max-time 4 "$address/_oblakohub/tls-ready" 2>/dev/null)" == "oblakohub-tls-ready" ]]; then
    ready=true
    break
  fi
  sleep 2
done
if [[ "$ready" != true ]]; then
  printf 'HTTPS пока недоступен. Проверь публичный IP и входящие TCP-порты 80 и 443.\n' >&2
  "${compose[@]}" logs --tail=25 caddy >&2
  exit 1
fi

# A fresh process issues the pairing code only after HTTPS is available.
# Recreating the container keeps the named volume and any existing token.
"${compose[@]}" up -d --force-recreate cloud
ready=false
for ((i=0; i<30; i++)); do
  if [[ "$(curl --noproxy '*' -fsS --max-time 4 "$address/api/v1/health" 2>/dev/null)" == '{"apiVersion":1}' ]]; then
    ready=true
    break
  fi
  sleep 1
done
if [[ "$ready" != true ]]; then
  printf 'HTTPS работает, но API облака не отвечает.\n' >&2
  exit 1
fi

printf '\nАдрес подключения: %s\n' "$address"
code=$("${compose[@]}" logs --no-color --no-log-prefix cloud | sed -nE 's/^Код подключения \(15 минут\): ([[:xdigit:]]{32})\r?$/\1/p' | tail -n 1)
if [[ -n "$code" ]]; then
  printf 'Одноразовый код (15 минут): %s\n' "$code"
else
  printf 'Новый код не выдан. Если облако уже подключено, прежний токен продолжит работать.\n'
fi
