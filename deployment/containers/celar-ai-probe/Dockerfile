ARG CELAR_PROBE_BASE_IMAGE
FROM ${CELAR_PROBE_BASE_IMAGE}

RUN apk add --no-cache bash bind-tools ca-certificates curl jq \
    && addgroup -S -g 10001 celarprobe \
    && adduser -S -D -H -u 10001 -G celarprobe celarprobe
COPY celar-ai-private-network-probe.sh /usr/local/bin/celar-ai-private-network-probe.sh
RUN chmod 0555 /usr/local/bin/celar-ai-private-network-probe.sh
USER 10001:10001
ENTRYPOINT ["/usr/local/bin/celar-ai-private-network-probe.sh"]
