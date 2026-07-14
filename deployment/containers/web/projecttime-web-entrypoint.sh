#!/bin/sh
set -eu

resolver="${NGINX_RESOLVER:-}"

if [ -z "$resolver" ]; then
    resolver="$(
        awk '
            $1 == "nameserver" && NF >= 2 {
                print $2
                exit
            }
        ' /etc/resolv.conf
    )"
fi

if [ -z "$resolver" ]; then
    echo "ERROR: No DNS resolver was found in /etc/resolv.conf." >&2
    exit 1
fi

case "$resolver" in
    *:*)
        resolver="[$resolver]"
        ;;
esac

export NGINX_RESOLVER="$resolver"

echo "WEB_RUNTIME_DNS_RESOLVER_CONFIGURED=YES"

exec /docker-entrypoint.sh "$@"
