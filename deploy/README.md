# Blocker Relay Deployment

## One-time droplet setup (`root@julianoschroeder.com`)

1. Install the systemd unit:
   ```
   scp deploy/blocker-relay.service root@julianoschroeder.com:/etc/systemd/system/
   ssh root@julianoschroeder.com 'mkdir -p /opt/blocker-relay && systemctl daemon-reload && systemctl enable blocker-relay'
   ```

2. Add the nginx location block inside `server { server_name julianoschroeder.com … }`
   in `/etc/nginx/sites-enabled/julianoschroeder.com`. See `deploy/nginx-location-block.conf`.
   Then:
   ```
   ssh root@julianoschroeder.com 'nginx -t && systemctl reload nginx'
   ```

3. First deploy:
   ```
   ./scripts/deploy-relay.sh
   ```

## Subsequent deploys
Just run `./scripts/deploy-relay.sh`.

## Verify
```
curl -i https://julianoschroeder.com/blocker/ws-relay
# Expected: HTTP 400 "bad websocket upgrade" from HttpListener (because no Upgrade header)
ssh root@julianoschroeder.com 'curl -s http://127.0.0.1:3002/healthz'
# Expected: ok
```
