# GDriveWP (GG Drive)

<p align="center">
  <strong>Ứng dụng Google Drive không chính thức cho Windows Phone 8.1, giao diện mô phỏng ứng dụng Google Drive trên Android / iOS.</strong><br>
  <em>Viết bằng C# / XAML (WinRT, wpa81), tối ưu cho Nokia Lumia 1020. Cùng kiến trúc với GGTranslate và YTMusicWP.</em>
</p>

---

## Tải xuống

| Phiên bản | Kiến trúc | Tải về |
|---|---|---|
| **1.0.0.0** | ARM (Lumia 1020 và các máy Windows Phone 8.1 khác) | [GDriveWP_1.0.0.0_ARM.appx](https://github.com/Hoangxee/ggdriverWP/raw/main/releases/GDriveWP_1.0.0.0_ARM.appx) |

Gói `.appx` chưa ký, dùng để cài thử (sideload):
1. Trên điện thoại: **Cài đặt → Cập nhật & bảo mật → Dành cho nhà phát triển** → bật **Chế độ nhà phát triển** (Developer mode).
2. Cài bằng một trong hai cách:
   - Cắm USB và dùng **Windows Phone Application Deployment** (`WinAppDeployCmd.exe install -file GDriveWP_1.0.0.0_ARM.appx -ip <ip>`), hoặc
   - Chép tệp `.appx` vào điện thoại, mở bằng File Explorer rồi cài đặt.
3. Mở ứng dụng, làm theo phần [Đăng nhập Google](#đăng-nhập-google-quan-trọng) để nhập Client ID.

---

## Tính năng

### Giao diện kiểu Google Drive (Android, Material 3)
- Thanh tìm kiếm dạng viên thuốc với ảnh đại diện, thanh điều hướng dưới cùng **Trang chủ / Dấu sao / Đã chia sẻ / Tệp**, nút **+** nổi để tải lên hoặc tạo thư mục.
- Danh sách tệp với biểu tượng màu theo loại (Docs xanh, Sheets lục, Slides vàng, PDF đỏ…), hình thu nhỏ cho ảnh / video, dòng phụ "Bạn đã sửa • 12 thg 10 • 3,2 MB".
- Thư mục mở dạng đẩy trang, nút Back phần cứng đi ngược lại đúng thứ tự (thư mục → tab → thoát).
- Chế độ sáng / tối (bảng màu Google), font Google Sans Flex.

### Quản lý tệp (Google Drive API v3)
- Duyệt **Drive của tôi**, **Gần đây**, **Có gắn dấu sao**, **Đã chia sẻ với tôi**, **Thùng rác**; tải thêm khi cuộn; sắp xếp theo tên hoặc ngày sửa.
- **Tải lên** nhiều tệp (bộ chọn tệp của hệ thống, tải lên theo phiên có thể tiếp tục, từng khối 1 MB) và nhận tệp từ menu **Chia sẻ** của ứng dụng khác.
- **Tải xuống** tới vị trí tự chọn (điện thoại / thẻ nhớ); Google Docs / Sheets / Slides được xuất sang DOCX / XLSX / PPTX.
- Tạo thư mục, đổi tên, di chuyển (bộ chọn thư mục), tạo bản sao, gắn dấu sao, chuyển vào thùng rác, khôi phục, xoá vĩnh viễn, dọn sạch thùng rác.
- **Chia sẻ liên kết** (tuỳ chọn mở "bất kỳ ai có liên kết"), gửi qua bảng Chia sẻ của hệ thống (Windows Phone không có API clipboard).
- **Tìm kiếm** toàn Drive hoặc trong thư mục đang mở.
- Trang **Chi tiết** (loại, kích thước, ngày sửa, chủ sở hữu, vị trí, trạng thái chia sẻ).
- Bảng **hoạt động** hiển thị tiến trình tải lên / tải xuống, có nút huỷ.

### Xem trước
- **Ảnh**: phóng to / thu nhỏ bằng hai ngón, chạm để ẩn thanh tiêu đề (ảnh > 25 MB không xem trước).
- **Video / âm thanh**: phát bằng MediaElement với thanh điều khiển của hệ thống.
- **PDF** và **Google Docs / Sheets / Slides**: Windows Phone 8.1 không có bộ kết xuất PDF (`Windows.Data.Pdf` chỉ có trên Windows 8.1), nên ứng dụng hiện **ảnh xem trước trang đầu** do Google tạo (phóng to được) và nút **Mở toàn bộ** tải tệp (Google Docs xuất sang PDF) rồi giao cho trình đọc PDF của máy (PDF Reader / Adobe Reader).
- Các loại khác: **Mở bằng ứng dụng khác** (tải về bộ nhớ đệm rồi giao cho hệ thống) hoặc **Mở trên web**.

---

## Đăng nhập Google (quan trọng)

Google chặn đăng nhập OAuth trong trình duyệt nhúng (WebView), vì vậy ứng dụng dùng cùng cách với YTMusicWP: **OAuth Client ID của chính bạn** + đăng nhập ở ngoài ứng dụng.

### 1. Tạo Client ID (một lần, miễn phí)
1. Vào [Google Cloud Console](https://console.cloud.google.com/) → tạo project.
2. **APIs & Services → Library** → bật **Google Drive API**.
3. **OAuth consent screen** → loại External, thêm địa chỉ Gmail của bạn vào **Test users** (ứng dụng ở trạng thái Testing là đủ cho dùng cá nhân).
4. **Credentials → Create credentials → OAuth client ID**:
   - **Desktop app** → dùng được cách "Toàn quyền Drive (dán mã)".
   - **TVs and Limited Input devices** → dùng được cách "Mã thiết bị (QR)".
5. Mở ứng dụng → **Cài đặt** (ảnh đại diện) → dán **Client ID** và **Client secret** → Lưu.

### 2. Chọn cách đăng nhập
| Cách | Phạm vi | Thấy gì |
|---|---|---|
| **Mã thiết bị (QR)** – như YTMusicWP: quét QR / nhập mã tại google.com/device trên máy khác | `drive.file` (Google chỉ cho phép phạm vi này với device flow) | Chỉ các tệp do GG Drive tải lên / tạo |
| **Toàn quyền Drive (dán mã)** – mở trang đăng nhập Google trong trình duyệt của điện thoại, sau khi cho phép trình duyệt chuyển tới `http://localhost/?code=…` (trang lỗi là bình thường), sao chép địa chỉ và dán vào ứng dụng | `drive` | Toàn bộ Drive của tôi, Đã chia sẻ, Thùng rác |

Token được lưu trong `LocalSettings` và tự làm mới; Đăng xuất sẽ thu hồi token.

---

## Hạn chế
- Không có chỉnh sửa Google Docs / Sheets / Slides, không có bình luận, không đồng bộ ngoại tuyến, không hỗ trợ Shared drives (Team Drive).
- Thư viện mã hoá video của Windows Phone 8.1 chỉ phát MP4 (H.264 / AAC), WMV, 3GP; các định dạng khác báo "không phát được".
- Google Drive API "sensitive scope": khi dự án ở trạng thái Testing, Google hiện màn hình cảnh báo "ứng dụng chưa được xác minh" và refresh token hết hạn sau 7 ngày (phải đăng nhập lại). Đưa ứng dụng sang Production để bỏ giới hạn 7 ngày.
- Cần kết nối Internet.

---

## Build

Yêu cầu: Windows có **MSBuild 14.0** (Visual Studio 2015 Build Tools) và **Windows Phone 8.1 SDK**. Gói `.appx` được tạo ở dạng chưa ký.

```powershell
# Release | ARM -> GDriveWP\AppPackages\GDriveWP_<ver>_ARM_Test\GDriveWP_<ver>_ARM.appx
powershell -ExecutionPolicy Bypass -File build.ps1

# Build lại toàn bộ, rồi cài lên điện thoại đang cắm USB (đã mở khoá developer)
powershell -ExecutionPolicy Bypass -File build.ps1 -Rebuild -Deploy

# Debug | x86 cho emulator
powershell -ExecutionPolicy Bypass -File build.ps1 -Configuration Debug -Platform x86 -Deploy
```

- `packages\` (Newtonsoft.Json 13.0.4 và 3 gói System.*) không được commit; `build.ps1` tự sao chép từ `D:\lumia1020\YTMusicWP\packages` hoặc `D:\lumia1020\GGTranslate\packages`.
- Ảnh tile / splash được tạo bằng `powershell -ExecutionPolicy Bypass -File tools\make-assets.ps1`.
- Phiên bản nằm ở `Identity Version` trong `GDriveWP\Package.appxmanifest`.
- Nhật ký chẩn đoán: `LocalFolder\diag.log` (đọc bằng `ISETool ts appxfolder:Local de <product-id> <thư mục>`).

---

## Cấu trúc mã nguồn

```text
GDriveWP.sln, build.ps1, tools\make-assets.ps1
GDriveWP\
  App.xaml(.cs)            bảng màu sáng/tối, font; nhận kết quả bộ chọn tệp và Share target
  MainPage.xaml(.cs)       toàn bộ UI (một trang + các lớp phủ), trạng thái dùng chung, toast, hộp thoại
  Pages\MainPage.*.cs      Navigation (tab, thư mục, trang con, Back), Auth (device code / dán mã, tài khoản),
                           Browse (truy vấn, phân trang, hình thu nhỏ), FileOps (menu, đổi tên, di chuyển, xoá, chia sẻ, chi tiết),
                           Transfers (tải lên / tải xuống), Search, Viewer (ảnh / video / PDF), Settings
  Services\                GoogleAuth (OAuth), DriveApiClient (REST v3: list/get/create/update/delete/upload/download),
                           ThumbnailCache, FileTypeInfo (loại tệp, màu, định dạng xuất, kích thước, ngày), QrCodeGenerator, DiagLog
  Models\                  DriveFile, FileListPage, TransferItem, AccountInfo
  Converters\              BoolToVisibilityConverter
  Assets\                  21 ảnh tile/splash, Fonts\AppFont-Regular.ttf, AppFont-Medium.ttf, OFL.txt
```

Bộ nhớ đệm: `LocalCacheFolder\thumbs` (hình thu nhỏ), `LocalCacheFolder\preview` (tệp đang xem, tự dọn khi > 80 MB); cài đặt và token trong `LocalSettings`.

---

## Giấy phép và ghi công
- **Google Sans Flex** — SIL Open Font License 1.1 (`GDriveWP/Assets/Fonts/OFL.txt`).
- **Material Symbols** — Google, Apache License 2.0 (dữ liệu path của biểu tượng).
- Bộ sinh mã QR lấy từ YTMusicWP (ISO/IEC 18004, không phụ thuộc thư viện ngoài).
- "Google", "Google Drive" là nhãn hiệu của Google LLC. Dự án này không liên kết với Google.
