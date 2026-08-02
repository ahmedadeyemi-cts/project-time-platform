# syntax=docker/dockerfile:1

FROM node:24-alpine AS build

# Install dependencies from the lock file before copying the complete
# validation context so dependency caching remains effective.
WORKDIR /workspace

COPY src/frontend/project-time-web/package.json \
     src/frontend/project-time-web/package-lock.json \
     src/frontend/project-time-web/

WORKDIR /workspace/src/frontend/project-time-web

RUN npm ci --no-audit --no-fund

# Frontend production builds execute repository-wide governance validators.
# Copy the complete non-secret Docker build context into this disposable build
# stage so newly integrated modules, migrations, rollbacks, tests, and docs are
# validated without maintaining a brittle file-by-file allowlist. The runtime
# stage below still receives only the compiled static frontend.
WORKDIR /workspace
COPY . /workspace/

WORKDIR /workspace/src/frontend/project-time-web

RUN npm run build


FROM nginxinc/nginx-unprivileged:1.31.3-alpine AS runtime

ENV API_UPSTREAM=http://127.0.0.1:5080 \
    NGINX_ENVSUBST_FILTER="^(API_UPSTREAM|NGINX_RESOLVER)$"

USER root

COPY --from=build \
     /workspace/src/frontend/project-time-web/dist/ \
     /usr/share/nginx/html/

COPY deployment/containers/web/default.conf.template \
     /etc/nginx/templates/default.conf.template

COPY deployment/containers/web/projecttime-web-entrypoint.sh \
     /usr/local/bin/projecttime-web-entrypoint.sh

RUN chmod 0644 /etc/nginx/templates/default.conf.template \
    && chmod 0755 /usr/local/bin/projecttime-web-entrypoint.sh

USER 101

RUN test -r /etc/nginx/templates/default.conf.template \
    && test -x /usr/local/bin/projecttime-web-entrypoint.sh \
    && API_UPSTREAM=http://127.0.0.1:18080 \
       NGINX_RESOLVER=127.0.0.1 \
       /usr/local/bin/projecttime-web-entrypoint.sh nginx -t \
    && rm -f /etc/nginx/conf.d/default.conf

EXPOSE 8080

ENTRYPOINT ["/usr/local/bin/projecttime-web-entrypoint.sh"]

CMD ["nginx", "-g", "daemon off;"]
