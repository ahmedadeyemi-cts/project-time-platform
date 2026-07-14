# syntax=docker/dockerfile:1

FROM node:20-alpine AS build

WORKDIR /src

COPY src/frontend/project-time-web/package.json \
     src/frontend/project-time-web/package-lock.json ./

RUN npm ci --no-audit --no-fund

COPY src/frontend/project-time-web/ ./

RUN npm run build


FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime

ENV API_UPSTREAM=http://127.0.0.1:5080 \
    NGINX_ENVSUBST_FILTER="^(API_UPSTREAM|NGINX_RESOLVER)$"

COPY --from=build /src/dist/ /usr/share/nginx/html/

COPY deployment/containers/web/default.conf.template \
     /etc/nginx/templates/default.conf.template

COPY deployment/containers/web/projecttime-web-entrypoint.sh \
     /usr/local/bin/projecttime-web-entrypoint.sh

RUN test -x /usr/local/bin/projecttime-web-entrypoint.sh \
    && API_UPSTREAM=http://127.0.0.1:18080 \
       NGINX_RESOLVER=127.0.0.1 \
       /usr/local/bin/projecttime-web-entrypoint.sh nginx -t \
    && rm -f /etc/nginx/conf.d/default.conf

EXPOSE 8080

ENTRYPOINT ["/usr/local/bin/projecttime-web-entrypoint.sh"]

CMD ["nginx", "-g", "daemon off;"]
