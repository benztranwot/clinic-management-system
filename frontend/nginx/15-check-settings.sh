#!/bin/sh
# Stop the container before nginx starts with an incomplete or unsafe header policy.
set -eu

case "${AUTH0_DOMAIN:-}" in
    "" )
        echo "Set AUTH0_DOMAIN to the Auth0 tenant host, for example your-tenant.us.auth0.com." >&2
        exit 1 ;;
esac

# A host name only: no scheme, path, port, spaces, quotes, or semicolons.
if ! printf '%s' "$AUTH0_DOMAIN" | grep -Eq '^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)+$'; then
    echo "AUTH0_DOMAIN must be a host name without https:// or a path." >&2
    exit 1
fi

# An IPv4 or IPv6 address or CIDR range.
if ! printf '%s' "${TRUSTED_PROXY_CIDR:-}" | grep -Eq '^[0-9A-Fa-f:.]+(/[0-9]{1,3})?$'; then
    echo "TRUSTED_PROXY_CIDR must be an IP address or CIDR range, for example 10.0.0.0/8." >&2
    exit 1
fi
