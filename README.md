<h1 align="center">☁️ OblakoHub Server</h1>

<p align="center"><b>Своё облако на своём устройстве</b></p>

<p align="center">
  <img alt="C#" src="https://img.shields.io/badge/C%23-512BD4?style=flat-square">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet">
  <img alt="Docker" src="https://img.shields.io/badge/Docker-2496ED?style=flat-square&logo=docker&logoColor=white">
</p>

**OblakoHub Server** превращает компьютер или сервер в личное файловое хранилище. Можно создавать папки, загружать файлы, скачивать их и перемещать внутри облака.

> [!NOTE]
> Сейчас доступен серверный API. Подключение через интерфейс OblakoHub, установка по SSH и готовый Docker-образ ещё в разработке. Ниже можно собрать и проверить сервер самостоятельно.

## 🚀 Запуск за три шага

**Что понадобится** — Docker и локальная копия репозитория. Открой терминал в папке с `Dockerfile`.

### 1. Собери образ

```sh
docker build -t oblakohub-server:dev .
```

### 2. Запусти облако

```sh
docker run -d --name oblakohub-server -p 127.0.0.1:8080:8080 -v oblakohub-data:/data oblakohub-server:dev
```

Облако будет доступно на этом компьютере по адресу `http://127.0.0.1:8080`. Файлы и данные доступа хранятся в томе `oblakohub-data`.

### 3. Проверь запуск и получи код

Открой в браузере [страницу проверки](http://127.0.0.1:8080/api/v1/health). Если видишь `{"apiVersion":1}`, сервер отвечает.

```sh
docker logs oblakohub-server
```

В журнале первого запуска появится **одноразовый код подключения**. Он действует 15 минут. Если срок истёк и код ещё никто не использовал, выполни `docker restart oblakohub-server` и снова открой журнал. Код и токен храни у себя.

## 🔑 Первое подключение

Скопируй код из журнала. Подставь его вместо `КОД` и отправь запрос:

```sh
curl -X POST http://127.0.0.1:8080/api/v1/pair --json '{"code":"КОД"}'
```

В Windows PowerShell используй `curl.exe` или отправь JSON через любой HTTP-клиент. Сервер вернёт поле `token`. Код после обмена перестанет работать. Сохрани токен и проверь доступ к корню облака:

```sh
curl -H "Authorization: Bearer ТОКЕН" http://127.0.0.1:8080/api/v1/files
```

Новый том вернёт пустой список `[]`.

## 🌍 Первый запуск на VPS с Ubuntu 24.04

Для этого теста понадобятся VPS с **Ubuntu 24.04 LTS**, публичный IPv4, SSH-доступ и домен или поддомен. В примерах замени `ubuntu` на логин из панели провайдера, `203.0.113.10` на IP своей VPS и `cloud.example.com` на свой домен. Если провайдер выдал SSH-порт, отличный от 22, добавь `-p ПОРТ` к `ssh` и `-P ПОРТ` к `scp`.

### 1. Подключись к VPS и установи Docker

На своём компьютере открой PowerShell и выполни:

```powershell
ssh ubuntu@203.0.113.10
```

Следующие команды выполняй **внутри SSH-сеанса на VPS**. Они добавляют официальный репозиторий Docker для Ubuntu 24.04 и устанавливают Docker вместе с Compose.

```bash
sudo apt update
sudo apt install -y ca-certificates curl
sudo install -d -m 0755 /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu noble stable" | sudo tee /etc/apt/sources.list.d/docker.list
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl enable --now docker
sudo docker compose version
```

Последняя команда должна вывести версию Docker Compose. [Инструкция Docker для Ubuntu](https://docs.docker.com/engine/install/ubuntu/)

### 2. Скачай исходники и отправь их на VPS

Открой [репозиторий OblakoHub Server](https://github.com/vZidv/OblakoHub-Server) под своим аккаунтом GitHub. Нажми **Code → Download ZIP** и распакуй архив. Репозиторий приватный, поэтому скачивай его на компьютере, где у тебя есть доступ.

Для теста текущей локальной версии можешь взять исходники прямо из `E:\Projects\OblakoHub.Server`. Убедись, что в выбранной папке есть `compose.yaml`, `Caddyfile`, `.env.example` и `Dockerfile`. Если этих файлов пока нет в ZIP, используй локальную копию.

Открой **второе окно PowerShell на своём компьютере**. Перейди в распакованную папку или в локальный проект. Отправь файлы на VPS:

```powershell
Set-Location 'E:\Projects\OblakoHub.Server'
ssh ubuntu@203.0.113.10 'mkdir -p ~/oblakohub-server'
scp Dockerfile .dockerignore .env.example compose.yaml Caddyfile OblakoHub.Server.csproj Program.cs ubuntu@203.0.113.10:~/oblakohub-server/
```

### 3. Настрой домен и запусти контейнеры

В панели DNS создай **A-запись** для поддомена и укажи публичный IPv4 VPS. В сетевом экране провайдера открой входящие TCP-порты **80 и 443**. Если на VPS уже включён UFW, проверь, что SSH разрешён, затем разреши `80/tcp` и `443/tcp`. Caddy использует эти порты для выпуска сертификата и работы HTTPS. [Документация Caddy](https://caddyserver.com/docs/automatic-https)

Вернись в окно с SSH-сеансом. В команде ниже укажи свой домен без `https://`:

```bash
cd ~/oblakohub-server
printf '%s\n' 'CLOUD_DOMAIN=cloud.example.com' > .env
sudo docker compose up -d --build
sudo docker compose ps
```

На своём компьютере открой `https://cloud.example.com/api/v1/health`, подставив свой домен. Ожидаемый ответ — `{"apiVersion":1}`. Если HTTPS не открывается, проверь DNS и порты, затем посмотри журнал Caddy через `sudo docker compose logs --tail=50 caddy`.

### 4. Подключи облако в OblakoHub

Когда проверка HTTPS пройдёт, получи код в SSH-сеансе:

```bash
sudo docker compose logs --tail=50 cloud
```

Код действует **15 минут с запуска сервера**. Если он истёк до первого подключения, выполни `sudo docker compose restart cloud` и снова открой журнал. Возьми последний код.

На своём компьютере открой **Добавить облако → Собственное облако**. Введи название, HTTPS-адрес своего домена и одноразовый код. После подключения загрузи небольшой файл, скачай его и проверь содержимое.

Файлы и данные подключения хранятся в томе `cloud-data`. Команда `sudo docker compose down` останавливает облако и сохраняет данные. Флаг `-v` удаляет тома вместе с данными.

## 📦 Как сохранить файлы

При пересоздании контейнера сохраняй его том: `oblakohub-data` при локальном запуске или `cloud-data` в Docker Compose. Его же стоит включить в резервную копию. Сервер поддерживает одно подключение. Если потерял токен, останови сервер, удали файл `credentials.json` из тома и запусти сервер снова. Старый токен перестанет работать, а новый код появится в журнале.

<details>
<summary>🛠️ Справочник API</summary>

| Запрос | Действие |
| --- | --- |
| `GET /api/v1/files?path=Папка` | Список элементов. Для корня опустите параметр. |
| `POST /api/v1/files/folder?path=Папка` | Создать папку. |
| `PUT /api/v1/files/content?path=Папка/файл.png` | Загрузить файл. Тело запроса содержит байты файла. |
| `GET /api/v1/files/content?path=Папка/файл.png` | Скачать файл. |
| `POST /api/v1/files/move` | Переместить или переименовать. JSON содержит поля `from` и `to`. |
| `DELETE /api/v1/files?path=Папка/файл.png` | Удалить файл или пустую папку. |

Пути относительны корню. Вложенные сегменты разделяются символом `/`. Кодируйте значения query-параметров при отправке. Запись поверх существующего файла возвращает конфликт.

</details>
