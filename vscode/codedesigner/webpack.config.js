//@ts-check

'use strict';

const path = require('path');
const CopyWebpackPlugin = require('copy-webpack-plugin');
const { AngularWebpackPlugin } = require('@ngtools/webpack');
// The Angular Linker processes partially-compiled Angular npm packages (those that still
// contain ɵɵngDeclare* calls) and converts them to fully-compiled ivy code at build time.
// Without this, the runtime would fall back to the JIT compiler — which is blocked by
// VS Code's 'unsafe-eval' CSP — and throw "PlatformLocation needs to be compiled by JIT".
// We use the `default` export which is a standard babel plugin (no webpack fileSystem needed).
const { default: angularLinkerPlugin } = require('@angular/compiler-cli/linker/babel');

//@ts-check
/** @typedef {import('webpack').Configuration} WebpackConfig **/

/** @type WebpackConfig */
const extensionConfig = {
  target: 'node', // VS Code extensions run in a Node.js-context 📖 -> https://webpack.js.org/configuration/node/
	mode: 'none', // this leaves the source code as close as possible to the original (when packaging we set this to 'production')

  entry: './src/extension.ts', // the entry point of this extension, 📖 -> https://webpack.js.org/configuration/entry-context/
  output: {
    // the bundle is stored in the 'dist' folder (check package.json), 📖 -> https://webpack.js.org/configuration/output/
    path: path.resolve(__dirname, 'dist'),
    filename: 'extension.js',
    libraryTarget: 'commonjs2'
  },
  externals: {
    vscode: 'commonjs vscode' // the vscode-module is created on-the-fly and must be excluded. Add other modules that cannot be webpack'ed, 📖 -> https://webpack.js.org/configuration/externals/
    // modules added here also need to be added in the .vscodeignore file
  },
  resolve: {
    // support reading TypeScript and JavaScript files, 📖 -> https://github.com/TypeStrong/ts-loader
    extensions: ['.ts', '.js']
  },
  module: {
    rules: [
      {
        test: /\.ts$/,
        exclude: /node_modules/,
        use: [
          {
            loader: 'ts-loader'
          }
        ]
      },
      {
        // Load .html and .css files as raw string content (no processing).
        // Import them in TypeScript as: import template from './foo.html';
        // See src/declarations.d.ts for the TypeScript module declarations.
        test: /\.(html|css)$/,
        exclude: /node_modules/,
        type: 'asset/source'
      }
    ]
  },
  devtool: 'nosources-source-map',
  infrastructureLogging: {
    level: "log", // enables logging required for problem matchers
  },
  plugins: [
    new CopyWebpackPlugin({
      patterns: [
        {
          from: 'src/language-configuration.json',
          to: 'language-configuration.json'
        },
        {
          from: 'src/resources/*.svg',
          to: 'resources/[name][ext]'
        },
        {
          from: 'src/resources/*.png',
          to: 'resources/[name][ext]'
        },
        {
          from: 'src/resources/*.webp',
          to: 'resources/[name][ext]'
        }
      ]
    })
  ]
};
/** @type WebpackConfig */
const webviewConfig = {
  target: 'web',
  mode: 'none',
  entry: {
    'disasm-panel':    './src/webview/disasm-panel/main.ts',
    'disasm-editor':   './src/webview/disasm-editor/main.ts',
    'label-manager':   './src/webview/label-manager/main.ts',
    'agent-ctrl-panel': './src/webview/agent-ctrl-panel/main.ts',
    'compat-browser':  './src/webview/compat-browser/main.ts',
    'cheats-editor':   './src/webview/cheats-editor/main.ts',
    'packet-viewer':      './src/webview/packet-viewer/main.ts',
    'debug-commands':     './src/webview/debug-commands/main.ts',
    'snapshot-manager':   './src/webview/snapshot-manager/main.ts',
    'diff-viewer':        './src/webview/diff-viewer/main.ts',
    'joker-maker':        './src/webview/joker-maker/main.ts',
    'structure-visualizer': './src/webview/structure-visualizer/main.ts',
    'memory-card-browser': './src/webview/memory-card-browser/main.ts',
    'hdd-browser': './src/webview/hdd-browser/main.ts',
    'zipper-workbench': './src/webview/zipper-workbench/main.ts',
    'welcome':         './src/webview/welcome/main.ts',
  },
  output: {
    path: path.resolve(__dirname, 'dist/webview'),
    filename: '[name].js',
  },
  resolve: {
    extensions: ['.ts', '.js'],
  },
  module: {
    rules: [
      {
        // Run the Angular Linker on Angular packages from node_modules.
        // Angular npm packages ship in "partial compilation" format (ɵɵngDeclare* calls).
        // The linker converts them to fully-compiled ivy code so that the JIT compiler
        // is never invoked at runtime.
        test: /\.[cm]?js$/,
        include: /node_modules[\\/]@angular/,
        use: {
          loader: 'babel-loader',
          options: {
            plugins: [angularLinkerPlugin],
            compact: false,
          },
        },
      },
      {
        // @ngtools/webpack handles all TypeScript files for the webview (AOT compilation).
        // This pre-compiles Angular templates at build time, eliminating the need for
        // the JIT compiler at runtime and avoiding 'unsafe-eval' CSP violations.
        test: /\.[cm]?[jt]sx?$/,
        loader: '@ngtools/webpack',
        exclude: /node_modules/,
      },
      {
        test: /\.css$/,
        use: ['style-loader', 'css-loader'],
        exclude: /node_modules/,
      },
    ],
  },
  plugins: [
    // AngularWebpackPlugin drives AOT compilation for all three webview entry points.
    new AngularWebpackPlugin({
      tsconfig: path.resolve(__dirname, 'tsconfig.webview.json'),
      jitMode: false,
    }),
  ],
  devtool: false,
};

module.exports = [ extensionConfig, webviewConfig ];
