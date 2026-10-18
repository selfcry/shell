#!/usr/bin/env bash
# Установка сервера клуба на Ubuntu/Debian VPS. Запускать от root в папке с этим скриптом после сборки:
#   dotnet publish src/ClubShell.Server -c Release -r linux-x64 --self-contained true -o publish   (на вашем компьютере)
#   scp -r publish deploy root@<ip-vps>:/root/   затем на VPS:  bash /root/deploy/install-server.sh
set -euo pipefail
id clubshell &>/dev/null || useradd --system --home /var/lib/clubshell --shell /usr/sbin/nologin clubshell
mkdir -p /opt/clubshell-server /var/lib/clubshell
cp -r "$(dirname "$0")/../publish/." /opt/clubshell-server/
chmod +x /opt/clubshell-server/ClubShell.Server
chown -R clubshell:clubshell /var/lib/clubshell
cp "$(dirname "$0")/clubshell-server.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now clubshell-server
sleep 3
echo "Сервер запущен. Ключ администратора: /var/lib/clubshell/admin-key.txt"
echo "Дальше: поставьте Caddy (apt install caddy), положите deploy/Caddyfile в /etc/caddy/Caddyfile со своим доменом и выполните: systemctl reload caddy"
echo "Откройте порты 80 и 443 в файрволе (ufw allow 80,443/tcp). Порт 5080 наружу не открывайте."
