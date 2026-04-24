### 🚀 What's new (Release 2.3.0)

* **Reworked Chrome/Edge address bar handling:** when the omnibox exposes a writable UI Automation ValuePattern, corrections are applied silently via direct UIA — no Ctrl+A, no visible selection flicker, no clipboard manipulation.
* **Clipboard-based fallback retained:** when ValuePattern is unavailable, the full-text rewrite path (Ctrl+A → copy → tokenize → paste) still handles the edge cases.
* **Stricter Chrome garbage-scan detection:** when ≥3 sequential scan codes are observed in the buffer, the VK interpretation is preferred — reduces spurious conversions in Electron-like windows.
* **New heuristics** for short Ukrainian words ending in `-х` plus protections for short technical acronyms against erroneous EN→UA conversion.
* **Extended regression coverage** for real-world user-reported scenarios.
