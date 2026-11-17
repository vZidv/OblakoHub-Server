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
sudo apt update
sudo apt install -y ca-certificates curl git
sudo install -d -m 0755 /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu noble stable" | sudo tee /etc/apt/sources.list.d/docker.list
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl enable --now docker
```

Если ты вошёл как `root` и `sudo` отсутствует, вводи команды без `sudo`. [Установка Docker на Ubuntu](https://docs.docker.com/engine/install/ubuntu/)

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
