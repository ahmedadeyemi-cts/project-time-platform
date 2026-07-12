# syntax=docker/dockerfile:1

FROM node:20-alpine AS build
WORKDIR /src

COPY src/frontend/project-time-web/package.json src/frontend/project-time-web/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY src/frontend/project-time-web/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime

ENV API_UPSTREAM=http://ca-phd-test-api-westus3 \
    NGINX_ENVSUBST_FILTER=^API_UPSTREAM$

COPY --from=build /src/dist/ /usr/share/nginx/html/
COPY deployment/containers/web/default.conf.template /etc/nginx/templates/default.conf.template

EXPOSE 8080
