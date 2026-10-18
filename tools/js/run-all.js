// Runs every browser-script test in this folder. Run: node tools/js/run-all.js (Node 18 or newer, no packages).
const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

let failed = 0;
for (const file of fs.readdirSync(__dirname).filter(f => f.endsWith('.test.js')).sort()) {
    const result = spawnSync(process.execPath, [path.join(__dirname, file)], { stdio: 'inherit' });
    if (result.status !== 0) failed++;
}
console.log(failed ? `${failed} test file(s) failed` : 'all browser-script tests passed');
process.exit(failed ? 1 : 0);
