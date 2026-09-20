# Dev-mode image: runs the Vite dev server, matching `npm run dev` in the
# README rather than a production build - adapter-auto (frontend/vite.config.ts)
# needs a specific adapter (e.g. adapter-node) to build for a plain container,
# which is a separate change from wiring up docker-compose.
FROM node:22-alpine
WORKDIR /app

COPY frontend/package.json frontend/package-lock.json ./
# Not `npm ci`: package-lock.json is currently out of sync with package.json
# (missing/mismatched @emnapi/* optional deps of esbuild/rollup), which `ci`
# refuses to run against regardless of platform. `install` tolerates it.
RUN npm install

COPY frontend/ .

EXPOSE 5173
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0"]
