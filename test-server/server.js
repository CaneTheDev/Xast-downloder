const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = 3000;
const VIDEO_FILE = path.join(__dirname, 'Royal.Pains.S05E01.720p.WEB-HD.x264-Pahe.in.mkv');

const server = http.createServer((req, res) => {
    const stat = fs.statSync(VIDEO_FILE);
    const fileSize = stat.size;

    // CORS headers
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, HEAD, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Range');

    if (req.method === 'OPTIONS') {
        res.writeHead(200);
        res.end();
        return;
    }

    if (req.method === 'HEAD') {
        res.writeHead(200, {
            'Content-Length': fileSize,
            'Content-Type': 'video/x-matroska',
            'Accept-Ranges': 'bytes',
            'Content-Disposition': `attachment; filename="${path.basename(VIDEO_FILE)}"`
        });
        res.end();
        return;
    }

    if (req.method === 'GET') {
        const range = req.headers.range;

        if (range) {
            const parts = range.replace(/bytes=/, '').split('-');
            const start = parseInt(parts[0], 10);
            const end = parts[1] ? parseInt(parts[1], 10) : fileSize - 1;
            const chunkSize = (end - start) + 1;

            const fileStream = fs.createReadStream(VIDEO_FILE, { start, end });

            res.writeHead(206, {
                'Content-Range': `bytes ${start}-${end}/${fileSize}`,
                'Accept-Ranges': 'bytes',
                'Content-Length': chunkSize,
                'Content-Type': 'video/x-matroska'
            });

            fileStream.pipe(res);
        } else {
            res.writeHead(200, {
                'Content-Length': fileSize,
                'Content-Type': 'video/x-matroska',
                'Accept-Ranges': 'bytes'
            });

            fs.createReadStream(VIDEO_FILE).pipe(res);
        }
    }
});

server.listen(PORT, '0.0.0.0', () => {
    console.log(`\n🚀 Server running on http://localhost:${PORT}`);
    console.log(`📹 Download URL: http://localhost:${PORT}/`);
    console.log(`📦 File: ${path.basename(VIDEO_FILE)}`);
    console.log(`📊 Size: ${(fs.statSync(VIDEO_FILE).size / (1024 * 1024)).toFixed(2)} MB`);
    console.log(`\n✅ Ready for multi-threaded downloads!\n`);
});
