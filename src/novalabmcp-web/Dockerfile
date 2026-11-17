FROM node:24-alpine AS build
WORKDIR /app
ARG NEXT_PUBLIC_ENTRA_CLIENT_ID=
ARG NEXT_PUBLIC_ENTRA_TENANT_ID=
ARG NEXT_PUBLIC_ENTRA_API_SCOPE=
ARG NEXT_PUBLIC_API_URL=
ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL
ENV NEXT_PUBLIC_ENTRA_CLIENT_ID=$NEXT_PUBLIC_ENTRA_CLIENT_ID
ENV NEXT_PUBLIC_ENTRA_TENANT_ID=$NEXT_PUBLIC_ENTRA_TENANT_ID
ENV NEXT_PUBLIC_ENTRA_API_SCOPE=$NEXT_PUBLIC_ENTRA_API_SCOPE
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN mkdir -p public && npm run build

FROM node:24-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production
ENV HOSTNAME=0.0.0.0
ENV PORT=3000
COPY --from=build --chown=node:node /app/package.json /app/package-lock.json ./
COPY --from=build --chown=node:node /app/node_modules ./node_modules
COPY --from=build --chown=node:node /app/.next ./.next
COPY --from=build --chown=node:node /app/public ./public
USER node
EXPOSE 3000
CMD ["npm", "run", "start"]
