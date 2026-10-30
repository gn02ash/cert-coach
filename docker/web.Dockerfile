# Build context: repository root.
FROM node:24-alpine AS build
WORKDIR /repo/web

COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
# The "Use the sample outline" button imports the synthetic outline from samples/.
COPY samples/ /repo/samples/
RUN npm run build

FROM nginx:1.28-alpine
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /repo/web/dist /usr/share/nginx/html
EXPOSE 80
