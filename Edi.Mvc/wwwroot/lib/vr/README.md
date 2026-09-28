# Locally served VR dependencies

| Dependency | Pinned version | License |
| --- | --- | --- |
| [Three.js](https://github.com/mrdoob/three.js/tree/r180) | 0.180.0 (r180) | MIT, see `THREE-LICENSE.txt` |
| [html2canvas](https://github.com/niklasvh/html2canvas/tree/v1.4.1) | 1.4.1 | MIT, see `HTML2CANVAS-LICENSE.txt` |

Unmodified distribution builds were obtained from the version-pinned npm CDN:
`https://cdn.jsdelivr.net/npm/three@0.180.0/build/three.module.min.js`,
`https://cdn.jsdelivr.net/npm/three@0.180.0/build/three.core.min.js` and
`https://cdn.jsdelivr.net/npm/html2canvas@1.4.1/dist/html2canvas.min.js`.
License files come from the corresponding upstream Git tags. Serve both Three
files together: its module build imports its core build by relative URL.
The application uses these local copies and makes no CDN request at runtime.
