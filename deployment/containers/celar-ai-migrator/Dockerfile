ARG CELAR_MIGRATOR_BASE_IMAGE
FROM ${CELAR_MIGRATOR_BASE_IMAGE}

RUN apk add --no-cache bash coreutils
WORKDIR /opt/projectpulse/release

COPY release-commit .projectpulse-release-commit
COPY migrations/ database/migrations/
COPY celar-ai-apply-required-migrations.sh /usr/local/bin/celar-ai-apply-required-migrations.sh

RUN chmod 0555 /usr/local/bin/celar-ai-apply-required-migrations.sh \
    && chmod 0444 .projectpulse-release-commit database/migrations/*

USER postgres
ENTRYPOINT ["/usr/local/bin/celar-ai-apply-required-migrations.sh", "/opt/projectpulse/release"]
