FROM postgres:16-alpine

RUN apk add --no-cache bash coreutils

WORKDIR /opt/projectpulse/release

COPY release-commit .projectpulse-release-commit
COPY migrations/ database/migrations/
COPY apply-pr55-test-migrations.sh /usr/local/bin/apply-pr55-test-migrations.sh

RUN chmod 0555 /usr/local/bin/apply-pr55-test-migrations.sh \
    && chmod 0444 .projectpulse-release-commit database/migrations/*.sql

ENTRYPOINT ["/usr/local/bin/apply-pr55-test-migrations.sh", "/opt/projectpulse/release"]
