FROM node:24.22.3-bookworm-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci
COPY . .

# Vite embeds these public Auth0 settings into the browser bundle.
ARG VITE_AUTH0_DOMAIN
ARG VITE_AUTH0_CLIENT_ID
ARG VITE_AUTH0_AUDIENCE
RUN npm run build

FROM nginx:1.30.8-alpine
# AUTH0_DOMAIN is required at runtime for the Content Security Policy.
# TRUSTED_PROXY_CIDR names the proxy allowed to report the visitor's address;
# the loopback default trusts no external proxy.
ENV NGINX_ENVSUBST_FILTER="^(AUTH0_DOMAIN|TRUSTED_PROXY_CIDR)$" \
    TRUSTED_PROXY_CIDR="127.0.0.1/32"
RUN rm -f /etc/nginx/conf.d/default.conf
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY nginx/security-headers.conf nginx/static-compression.conf /etc/nginx/snippets/
COPY --chmod=755 nginx/15-check-settings.sh /docker-entrypoint.d/15-check-settings.sh
COPY --from=build /app/dist /usr/share/nginx/html
