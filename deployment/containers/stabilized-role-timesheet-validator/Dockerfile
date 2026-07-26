FROM postgres:16-alpine

RUN apk add --no-cache bash

COPY scripts/verify-stabilized-role-permission-timesheet-test.sh /usr/local/bin/verify-stabilized-role-permission-timesheet-test.sh
RUN chmod 0555 /usr/local/bin/verify-stabilized-role-permission-timesheet-test.sh

ENTRYPOINT ["/usr/local/bin/verify-stabilized-role-permission-timesheet-test.sh"]
