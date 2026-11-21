<h1 align="center">☁️ OblakoHub Server</h1>

<p align="center"><b>Своё облако на своей VPS</b></p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet">
  <img alt="Docker" src="https://img.shields.io/badge/Docker-2496ED?logo=docker&logoColor=white">
</p>

## 🚀 Установка в два шага

Открой SSH-сеанс на чистой **Ubuntu 24.04 LTS**. Нужен публичный IPv4. В панели VPS разреши входящие TCP-порты **80 и 443**. Оба блока выполняй на VPS.

### 1. Установи Docker и Git

```bash
wget -O install-docker.sh https://raw.githubusercontent.com/vZidv/OblakoHub-Server/main/install-docker.sh &&
bash install-docker.sh
```

Если на VPS нет wget, скачай тот же скрипт через `curl -fLo install-docker.sh URL`, подставив ссылку из блока. [Установка Docker на Ubuntu](https://docs.docker.com/engine/install/ubuntu/)

### 2. Запусти своё облако

```bash
git clone https://github.com/vZidv/OblakoHub-Server.git &&
cd OblakoHub-Server &&
bash install.sh
```

Введи публичный IPv4 из панели VPS, когда установщик спросит. Дождись **адреса подключения и одноразового кода**. Caddy получает и продлевает доверенный сертификат Let’s Encrypt для IP. [Сертификаты Let’s Encrypt для IP](https://letsencrypt.org/2026/01/15/6day-and-ip-general-availability)

В OblakoHub открой **Добавить облако → Собственное облако**, введи название, выданный HTTPS-адрес и код. Код действует 15 минут. Если срок истёк до подключения, снова запусти `bash install.sh` из папки проекта.

<details>
<summary>📦 Данные и остановка</summary>

Файлы и данные подключения хранятся в томе `cloud-data`. Команда `sudo docker compose down` останавливает сервисы и сохраняет том. Флаг `-v` удаляет тома с данными.

</details>
