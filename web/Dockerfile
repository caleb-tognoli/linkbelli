# Multi-stage build for the Linkbelli web app (SvelteKit + adapter-node). Context: web/.
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build
RUN npm prune --omit=dev

FROM node:22-alpine AS final
WORKDIR /app
ENV NODE_ENV=production
# adapter-node needs the build output, package.json, and production node_modules.
COPY --from=build /app/build ./build
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/package.json ./package.json
EXPOSE 3000
# Run as the non-root 'node' user shipped in the node image to limit blast radius.
USER node
CMD ["node", "build"]
