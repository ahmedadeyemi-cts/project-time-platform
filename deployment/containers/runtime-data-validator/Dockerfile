FROM postgres:16-alpine

RUN apk add --no-cache bash

COPY scripts/verify-direct-runtime-data-test.sh /usr/local/bin/verify-direct-runtime-data-test.sh
RUN chmod 0555 /usr/local/bin/verify-direct-runtime-data-test.sh

ENTRYPOINT ["/usr/local/bin/verify-direct-runtime-data-test.sh"]
