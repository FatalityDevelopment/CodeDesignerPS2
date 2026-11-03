/**
 * Module declarations for asset imports bundled as raw strings by webpack.
 * The webpack `asset/source` rule in webpack.config.js loads these file types
 * as their raw text content, so TypeScript needs to know they export a string.
 */

declare module '*.html' {
    const content: string;
    export default content;
}

declare module '*.css' {
    const content: string;
    export default content;
}
