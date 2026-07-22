#!/usr/bin/env bash
set -Eeuo pipefail

if [[ "$#" -ne 4 ]]; then
  echo 'Usage: build-pr55-acr-image.sh <registry> <repository:tag> <dockerfile> <context>' >&2
  exit 2
fi

ACR_NAME="$1"
IMAGE="$2"
DOCKERFILE="$3"
CONTEXT="$4"

[[ -n "$ACR_NAME" && -n "$IMAGE" ]] || {
  echo 'The ACR name and image tag are required.' >&2
  exit 2
}
[[ -f "$DOCKERFILE" ]] || {
  echo "The ACR build Dockerfile does not exist: $DOCKERFILE" >&2
  exit 2
}
[[ -d "$CONTEXT" ]] || {
  echo "The ACR build context does not exist: $CONTEXT" >&2
  exit 2
}

DIGEST="$(
  az acr build \
    --registry "$ACR_NAME" \
    --image "$IMAGE" \
    --file "$DOCKERFILE" \
    --no-logs \
    --query 'outputImages[0].digest' \
    --output tsv \
    "$CONTEXT"
)"

[[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] || {
  echo "The successful ACR build did not return a valid immutable digest for $IMAGE." >&2
  exit 1
}

printf '%s\n' "$DIGEST"
