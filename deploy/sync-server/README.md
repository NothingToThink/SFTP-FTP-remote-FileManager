# Сервер синхронизации SWINE

Нужен VPS с Docker и доменом, A-запись которого указывает на VPS (порты 80 и 443 открыты).

1. Укажите домен и запустите: `cd deploy/sync-server && echo "SWINE_DOMAIN=sync.example.com" > .env && docker compose up -d`
2. В SWINE откройте «Синхронизация» и введите `https://sync.example.com`.

HTTPS-сертификат Caddy получает сам. Данные (`app.db`, `app.key`, `jwt.key`) лежат в томе `swine-data`
и создаются при первом старте; ключ JWT можно задать переменной `Jwt__Key` (base64, от 32 байт).

Проверка без домена и HTTPS: `SWINE_DOMAIN=http://localhost docker compose up -d`, затем
`curl -X POST localhost/auth/register -H 'Content-Type: application/json' -d '{"username":"u","password":"p"}'` вернёт 201.
