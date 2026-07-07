FROM node:22 AS build
WORKDIR /src
COPY . .
RUN npm ci
RUN npx nx build web-internal

FROM nginx:alpine
COPY --from=build /src/dist/apps/web-internal /usr/share/nginx/html
COPY apps/web-internal/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
