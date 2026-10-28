# 第三方著作權與授權

AuriHelm 自行撰寫的程式碼採用根目錄 LICENSE 的 MIT 授權。以下第三方元件仍適用各自原始授權，本專案的授權不取代它們。

## 套件與建置工具

本產品使用 Microsoft.WindowsAppSDK、Microsoft.Windows.SDK.BuildTools、NAudio.Wasapi 與間接相依套件；精確版本由各專案的 packages.lock.json 記錄。NAudio 2.2.1 採 MIT 授權。

發佈腳本在 `third-party-licenses/` 保存可取得的原授權檔，以及 `packages.json` 的版本、作者、著作權、授權與來源資訊。此清單包含建置用相依性，不表示每個套件都作為執行檔交付。

NSIS 3.13 編譯器及 plug-in 各依原始授權適用 zlib/libpng、bzip2、LZMA CPL 等條款；安裝包附上 `third-party-licenses/NSIS-COPYING.txt` 完整說明。

## Windows HID 與相容互通部分

Windows HID 列舉及部分原生互通程式碼含 MIT 授權的改作。以下著作權及授權聲明適用於這些部分：

MIT License

Copyright (c) 2026 Ayerdi

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
