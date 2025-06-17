# NCM3 - Network Configuration Management System

NCM3 (Network Configuration Management) là hệ thống quản lý cấu hình mạng tự động, hỗ trợ sao lưu, khôi phục, phát hiện thay đổi và thông báo cho quản trị viên thông qua Telegram. Hệ thống hỗ trợ các thiết bị mạng Cisco và các thiết bị tương thích thông qua giao thức SSH và SNMP.



## Tính năng chính

- **Quản lý thiết bị**: Thêm, sửa, xóa và phân nhóm router/switch
- **Sao lưu tự động**: Lập lịch sao lưu cấu hình theo định kỳ
- **Phát hiện thay đổi**: Tự động phát hiện thay đổi cấu hình qua SNMP và SSH
- **Thông báo thời gian thực**: Gửi cảnh báo qua Telegram khi phát hiện thay đổi
- **Rollback nhanh chóng**: Khôi phục về phiên bản cấu hình trước đó
- **Kiểm tra tuân thủ**: Kiểm tra cấu hình thiết bị theo các quy tắc được định nghĩa sẵn(chưa hoàn thành)
- **Sao lưu đám mây**: Tích hợp với AWS S3 để lưu trữ cấu hình an toàn
-**Build-in CLI**: (chưa hoàn thành)
## Yêu cầu hệ thống

- **.NET 8.0** trở lên
- **SQL Server** (hoặc SQL Server Express)
- **Windows/Linux/macOS** (hỗ trợ đa nền tảng)
- **Docker** (tùy chọn, nếu muốn chạy container)(chưa setup)

## Hướng dẫn cài đặt

### 1. Cài đặt tự động với script

#### Trên Linux/macOS:

```bash
# Clone repository
git clone https://github.com/yourusername/NCM3.git
cd NCM3

# Cấp quyền thực thi cho script
chmod +x setup.sh

# Chạy script cài đặt
./setup.sh
```

#### Trên Windows:

```bash
# Clone repository
git clone https://github.com/yourusername/NCM3.git
cd NCM3

# Chạy script cài đặt
setup.bat
```

Script sẽ tự động:
- Cài đặt Entity Framework Tools
- Thêm các package SQL Server cần thiết
- Cài đặt thư viện SNMP
- Cài đặt AWS SDK
- Tạo file cấu hình mẫu
- Khởi tạo cơ sở dữ liệu
- Thiết lập môi trường Docker (nếu có)

### 2. Cài đặt thủ công từ mã nguồn

```bash
# Clone repository
git clone https://github.com/yourusername/NCM3.git
cd NCM3

# Khôi phục các package
dotnet restore

# Biên dịch dự án
dotnet build
```

### 3. Cấu hình Bảo mật

Dự án này sử dụng file cấu hình riêng để lưu trữ thông tin nhạy cảm (secrets) tách biệt với mã nguồn.

### Thiết lập ban đầu

1. Tạo file `appsettings.secrets.json` trong thư mục NCM3:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=YOUR_DB;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
  },
  "Telegram": {
    "BotToken": "YOUR_BOT_TOKEN",
    "ChatId": "YOUR_CHAT_ID",
    "UseProxy": false,//khuyến nghị nên dùng proxy nếu telegram bị chặn ở nước sở tại
    "ProxyApiUrl": "YOUR_PROXY_URL_IF_NEEDED"
  },
  "AWS": {
    "AccessKeyId": "YOUR_AWS_ACCESS_KEY_ID",
    "SecretAccessKey": "YOUR_AWS_SECRET_ACCESS_KEY",
    "Region": "YOUR_AWS_REGION",
    "S3": {
      "BucketName": "YOUR_S3_BUCKET_NAME"
    }
  },
  "EncryptionKey": "YOUR_ENCRYPTION_KEY_FOR_ROUTER_CREDENTIALS"
}
```

2. Thay thế các giá trị `YOUR_*` với thông tin bảo mật thực tế của bạn.

3. File này sẽ được tự động bỏ qua bởi Git (được định nghĩa trong `.gitignore`)

### Cấu hình cho môi trường phát triển

Nếu cần ghi đè các cài đặt cho môi trường phát triển, sử dụng file `appsettings.Development.json`.

### Lưu ý bảo mật

- **KHÔNG** commit file `appsettings.secrets.json` lên Git repository
- Chỉ chia sẻ file này qua kênh bảo mật với các thành viên đáng tin cậy trong team
- Trong môi trường sản xuất, nên sử dụng các giải pháp quản lý bảo mật như Azure Key Vault hoặc AWS Secrets Manager

## Cấu trúc Cấu hình

- `appsettings.json`: Cài đặt mặc định, chứa giá trị placeholder
- `appsettings.Development.json`: Ghi đè cài đặt cho môi trường phát triển  
- `appsettings.secrets.json`: Chứa các thông tin nhạy cảm (không đưa lên GitHub)

## Khởi động ứng dụng

### Sử dụng run scripts (Khuyến nghị)

NCM3 cung cấp các script chạy (run scripts) để quản lý vòng đời ứng dụng một cách dễ dàng.

#### Trên Linux/macOS:

```bash
# Cấp quyền thực thi cho script
chmod +x run.sh

# Khởi động NCM3
./run.sh start

# Các lệnh khác
./run.sh stop        # Dừng NCM3
./run.sh status      # Kiểm tra trạng thái
./run.sh logs        # Xem logs
./run.sh test        # Chạy test
./run.sh update      # Cập nhật database
./run.sh docker      # Quản lý Docker
./run.sh help        # Hiển thị trợ giúp
```

#### Trên Windows:

```powershell
# Sử dụng PowerShell script
cd ./NCM3 ;..\run-project.bat

### Khởi động thủ công

```bash
dotnet run --project NCM3/NCM3.csproj
```

Ứng dụng sẽ tự động ghép các cài đặt từ tất cả các file appsettings có sẵn theo thứ tự ưu tiên.

### 3. Cài đặt Database

```bash
# Tạo migration ban đầu (nếu chưa có)
dotnet ef migrations add InitialCreate --project NCM3

# Cập nhật database
dotnet ef database update --project NCM3
```

### 4. Khởi chạy với Docker (tùy chọn)

```bash
# Build Docker image
docker build -t ncm3 .

# Chạy container
docker run -d -p 8080:80 --name ncm3-app ncm3
```

## Hướng dẫn sử dụng

### 1. Đăng nhập hệ thống

- Truy cập ứng dụng tại địa chỉ `http://localhost:5500` (hoặc cổng đã cấu hình)
- Đăng nhập với tài khoản mặc định: `admin` / `admin@123` (nhớ đổi mật khẩu sau khi đăng nhập)

### 2. Thêm thiết bị mạng

1. Đi đến trang **Routers** > **Add New Router** (WebApp hiện tại chỉ hỗ trợ thiết bị/vendor Cisco)
2. Điền thông tin thiết bị:
   - Hostname
   - IP Address
   - Credentials (Username/Password)
   - Model (tùy chọn)
   - OS Version (tùy chọn)
3. Nhấn **Save** để thêm thiết bị

### 3. Sao lưu cấu hình

- **Sao lưu thủ công**: Chọn router > **Backup Now**
- **Lập lịch sao lưu**: Đi đến **Settings** > **Backup Schedule**
  - Cấu hình tần suất: Hàng ngày, hàng tuần, hoặc hàng tháng
  - Chọn thời điểm thực hiện

### 4. Phát hiện thay đổi

Hệ thống sẽ tự động phát hiện thay đổi cấu hình thông qua:
- **SNMP Polling**: Kiểm tra theo khoảng thời gian đã cấu hình
- **SSH Polling**: Quét định kỳ để phát hiện thay đổi

### 5. Khôi phục cấu hình

1. Đi đến trang **Routers** > Chọn router
2. Chọn tab **Configuration History**
3. Tìm phiên bản cần khôi phục và nhấn **Restore**
4. Xác nhận hành động

### 6. Cấu hình thông báo Telegram

1. Tạo bot Telegram và lấy Bot Token (xem hướng dẫn [tại đây](https://topdev.vn/blog/telegram-tao-bot-va-lam-vai-thu-vui-ve/))
2. Tạo nhóm Telegram và thêm bot vào nhóm
3. Lấy Chat ID của nhóm
4. Cập nhật thông tin trong `appsettings.secrets.json`

### 7. Cấu hình AWS S3 (tùy chọn)

1. Tạo bucket S3 trên AWS
2. Tạo IAM user với quyền truy cập bucket
3. Cập nhật thông tin Access Key và Secret Key trong `appsettings.secrets.json`

## Xử lý sự cố

### Vấn đề kết nối SNMP
- Kiểm tra community string
- Xác nhận port UDP 161 không bị chặn bởi firewall
- Đảm bảo router đã được cấu hình SNMP

### Vấn đề kết nối SSH
- Kiểm tra thông tin đăng nhập
- Đảm bảo port TCP 22 không bị chặn
- Kiểm tra log kết nối trong mục Logs

### Vấn đề thông báo Telegram
- Đảm bảo Bot Token và Chat ID chính xác
- Kiểm tra bot có quyền gửi tin nhắn trong nhóm
- Xem xét sử dụng proxy nếu bị hạn chế kết nối

## Hỗ trợ và đóng góp

Nếu bạn gặp vấn đề hoặc có đóng góp, vui lòng:
- Tạo Issue trên GitHub
- Gửi Pull Request với cải tiến
- Liên hệ qua email: sang59498@gmail.com

