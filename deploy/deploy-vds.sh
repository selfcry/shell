#!/usr/bin/env bash
# Обновление сервера клуба и веб-админки на VDS одной командой (запускать на своём компьютере из корня репозитория):
#   deploy/deploy-vds.sh            — собрать и выкатить сервер + веб-админку
#   deploy/deploy-vds.sh --admin    — только веб-админку (сервер не трогается)
#   deploy/deploy-vds.sh --web      — только сайт с личным кабинетом (web/, Next.js)
#   deploy/deploy-vds.sh --rollback — вернуть предыдущую версию сервера
# Нужно: ssh-доступ к хосту (по умолчанию алиас «vds», можно VDS=user@host), .NET 8 SDK и Node 20 локально.
# Перед заменой делается копия базы, после замены проверяется /health; если сервер не ответил — откат автоматически.
set -euo pipefail

VDS="${VDS:-vds}"
APP=/opt/clubshell-server
WEB=/var/www/clubshell-admin
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/build/deploy"
MODE="${1:-all}"

remote() { ssh "$VDS" "set -euo pipefail; $*"; }

if [[ "$MODE" == "--rollback" ]]; then
  remote "test -d $APP.prev || { echo 'Нет предыдущей версии'; exit 1; }
    systemctl stop clubshell-server
    rm -rf $APP.failed && mv $APP $APP.failed && mv $APP.prev $APP
    if [ -f /etc/systemd/system/clubshell-server.service.prev ]; then cp /etc/systemd/system/clubshell-server.service.prev /etc/systemd/system/clubshell-server.service; systemctl daemon-reload; fi
    systemctl start clubshell-server && sleep 3 && curl -fsS http://127.0.0.1:5080/health"
  echo; echo "Откат выполнен."
  exit 0
fi

rm -rf "$OUT" && mkdir -p "$OUT"

deploy_web() {
  echo "== Сборка сайта с кабинетом (web/)"
  (cd "$ROOT/web" && npm ci --silent && env -u CLUBSHELL_API npx next build --webpack >/dev/null)
  mkdir -p "$OUT/web"
  cp -r "$ROOT/web/.next/standalone/." "$OUT/web/"
  mkdir -p "$OUT/web/.next" && cp -r "$ROOT/web/.next/static" "$OUT/web/.next/static"
  cp -r "$ROOT/web/public" "$OUT/web/public"
  echo "== Выкладка сайта"
  rsync -a --delete "$OUT/web/" "$VDS:/opt/clubshell-web.new/"
  scp -q "$ROOT/deploy/clubshell-web.service" "$VDS:/etc/systemd/system/clubshell-web.service"
  remote "chown -R clubshell:clubshell /opt/clubshell-web.new
    rm -rf /opt/clubshell-web.prev; if [ -d /opt/clubshell-web ]; then mv /opt/clubshell-web /opt/clubshell-web.prev; fi; mv /opt/clubshell-web.new /opt/clubshell-web
    systemctl daemon-reload; systemctl enable -q clubshell-web; systemctl restart clubshell-web
    for i in 1 2 3 4 5 6 7 8 9 10; do curl -fsS -o /dev/null http://127.0.0.1:3100/cabinet && exit 0; sleep 2; done
    echo 'Сайт не ответил — откат'; journalctl -u clubshell-web -n 20 --no-pager
    if [ -d /opt/clubshell-web.prev ]; then rm -rf /opt/clubshell-web; mv /opt/clubshell-web.prev /opt/clubshell-web; systemctl restart clubshell-web; fi; exit 1"
}

if [[ "$MODE" == "--web" ]]; then
  deploy_web
  curl -fsS -o /dev/null -w "кабинет: %{http_code}\n" https://cryvx.ru/cabinet
  exit 0
fi

# ---------- веб-админка ----------
echo "== Сборка интерфейса"
(cd "$ROOT/ui" && npm ci --silent && npm run build --silent)
mkdir -p "$OUT/admin"
cp "$ROOT/ui/dist/admin.html" "$OUT/admin/index.html"
cp -r "$ROOT/ui/dist/assets" "$OUT/admin/assets"

echo "== Выкладка веб-админки"
rsync -a --delete "$OUT/admin/" "$VDS:$WEB.new/"
remote "rm -rf $WEB.prev; if [ -d $WEB ]; then mv $WEB $WEB.prev; fi; mv $WEB.new $WEB"

if [[ "$MODE" == "--admin" ]]; then
  curl -fsS -o /dev/null -w "веб-админка: %{http_code}\n" https://cryvx.ru/admin/
  exit 0
fi

# ---------- сервер ----------
echo "== Сборка сервера (linux-x64)"
dotnet publish "$ROOT/src/ClubShell.Server" -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:DebugType=none -o "$OUT/server" --nologo -v quiet

echo "== Выкладка сервера"
rsync -a --delete "$OUT/server/" "$VDS:$APP.new/"
UNIT=/etc/systemd/system/clubshell-server.service
remote "cp $UNIT $UNIT.prev 2>/dev/null || true"
scp -q "$ROOT/deploy/clubshell-server.service" "$VDS:$UNIT"
remote "chmod +x $APP.new/ClubShell.Server
  chmod 700 /var/lib/clubshell; chmod 600 /var/lib/clubshell/admin-key.txt /var/lib/clubshell/*.db* 2>/dev/null || true
  systemctl daemon-reload
  systemctl stop clubshell-server
  mkdir -p /var/lib/clubshell/backups
  cp /var/lib/clubshell/club.db /var/lib/clubshell/backups/pre-deploy-\$(date +%Y%m%d-%H%M%S).db
  ls -1t /var/lib/clubshell/backups/pre-deploy-*.db | tail -n +6 | xargs -r rm --
  chown -R clubshell:clubshell /var/lib/clubshell
  rm -rf $APP.prev; mv $APP $APP.prev; mv $APP.new $APP
  systemctl start clubshell-server"

deploy_web

echo "== Проверка"
for i in 1 2 3 4 5 6 7 8 9 10; do
  if remote "curl -fsS http://127.0.0.1:5080/health" >/dev/null 2>&1; then
    curl -fsS https://cryvx.ru/health; echo; echo "Готово."
    exit 0
  fi
  sleep 2
done

echo "Сервер не ответил после обновления — откатываю."
remote "journalctl -u clubshell-server -n 30 --no-pager || true
  systemctl stop clubshell-server
  rm -rf $APP.failed && mv $APP $APP.failed && mv $APP.prev $APP
  cp $UNIT.prev $UNIT 2>/dev/null && systemctl daemon-reload
  systemctl reset-failed clubshell-server || true
  systemctl start clubshell-server"
exit 1
