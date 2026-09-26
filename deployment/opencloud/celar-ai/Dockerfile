# syntax=docker/dockerfile:1
ARG UBUNTU_IMAGE=ubuntu:24.04
FROM ${UBUNTU_IMAGE}
ARG SOURCE_REVISION
RUN test -n "$SOURCE_REVISION" && echo "$SOURCE_REVISION" | grep -Eq '^[a-f0-9]{40}$'
LABEL org.opencontainers.image.revision=$SOURCE_REVISION
ENV DEBIAN_FRONTEND=noninteractive PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 OMP_THREAD_LIMIT=2
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates python3 python3-flask python3-requests python3-pil gunicorn \
    tesseract-ocr tesseract-ocr-eng poppler-utils fonts-dejavu-core \
    clamav clamav-daemon clamav-freshclam \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 celar && useradd --uid 10001 --gid 10001 --no-create-home celar \
    && mkdir -p /var/lib/celar-ai/gateway /run/celar-laya \
    && chown -R 10001:10001 /var/lib/celar-ai /var/lib/clamav /run/celar-laya
WORKDIR /opt/celar-ai/gateway
COPY deployment/oracle-celar/gateway/ /opt/celar-ai/gateway/
COPY deployment/oracle-celar/release.json /opt/opencloud/source-runtime.json
COPY deployment/opencloud/celar-ai/runtime.py deployment/opencloud/celar-ai/clamd.conf deployment/opencloud/celar-ai/freshclam.conf /opt/opencloud/
USER 10001:10001
ENTRYPOINT ["python3", "/opt/opencloud/runtime.py"]
CMD ["gateway"]
