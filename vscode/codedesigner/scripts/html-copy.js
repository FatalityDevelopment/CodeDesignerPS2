const fs = require('fs');
const path = require('path');

const srcRoot = path.join(__dirname, '..', 'src', 'providers');
const outRoot = path.join(__dirname, '..', 'out');

function copyHtmlFiles(srcDir, outDir) {
    if (!fs.existsSync(outDir)) {
        fs.mkdirSync(outDir, { recursive: true });
    }

    const entries = fs.readdirSync(srcDir, { withFileTypes: true });

    for (const entry of entries) {
        const srcPath = path.join(srcDir, entry.name);
        const outPath = path.join(outDir, entry.name);

        if (entry.isDirectory()) {
            // recurse into folder
            copyHtmlFiles(srcPath, outPath);
        } else if (entry.isFile() && entry.name.endsWith('.html')) {
            fs.copyFileSync(srcPath, outPath);
            console.log(`Copied ${srcPath} → ${outPath}`);
        }
    }
}

// Start copying
copyHtmlFiles(srcRoot, outRoot);
