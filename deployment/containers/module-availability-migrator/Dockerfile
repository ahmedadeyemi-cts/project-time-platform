FROM postgres:16-alpine

RUN apk add --no-cache bash coreutils

WORKDIR /opt/projectpulse/release

COPY release-commit .projectpulse-release-commit
COPY migrations/ database/migrations/
COPY apply-module-availability-test-migration.sh /usr/local/bin/apply-module-availability-test-migration.sh

RUN chmod 0555 /usr/local/bin/apply-module-availability-test-migration.sh \
    && chmod 0444 .projectpulse-release-commit database/migrations/*.sql database/migrations/SHA256SUMS

ENTRYPOINT ["/usr/local/bin/apply-module-availability-test-migration.sh", "/opt/projectpulse/release"]
