import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

const repository = fileURLToPath(new URL('../../', import.meta.url));

export default defineConfig({
    server: {
        fs: {
            allow: [repository],
        },
    },
    test: {
        include: ['*.test.js'],
        environment: 'node',
        coverage: {
            provider: 'v8',
            allowExternal: true,
            include: [`${repository}src/Chaos.Web/**/*.js`],
            reporter: ['text', 'cobertura'],
            reportsDirectory: `${repository}artifacts/coverage/js`,
        },
    },
});
