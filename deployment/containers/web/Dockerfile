# syntax=docker/dockerfile:1

FROM node:20-alpine AS build

# Preserve the repository-relative path used by the frontend validation
# scripts while copying only the repository files they inspect.
WORKDIR /workspace

COPY src/frontend/project-time-web/package.json \
     src/frontend/project-time-web/package-lock.json \
     src/frontend/project-time-web/

WORKDIR /workspace/src/frontend/project-time-web

RUN npm ci --no-audit --no-fund

WORKDIR /workspace

COPY src/frontend/project-time-web/ \
     src/frontend/project-time-web/

COPY src/backend/ProjectTime.Api/Program.cs \
     src/backend/ProjectTime.Api/Program.cs

COPY src/backend/ProjectTime.Api/Modules/IdentityProfileModule.cs \
     src/backend/ProjectTime.Api/Modules/IdentityProfileModule.cs

COPY docs/modules/module-062-unified-identity-profile/README.md \
     docs/modules/module-062-unified-identity-profile/README.md

WORKDIR /workspace/src/frontend/project-time-web

RUN npm run build


FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime

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
