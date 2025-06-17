#!/bin/bash

# NCM3 Setup Script
# This script installs all necessary dependencies and extensions for NCM3

# Text colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Print banner
echo -e "${BLUE}"
echo "==============================================================="
echo "                NCM3 - Setup & Installation                    "
echo "==============================================================="
echo -e "${NC}"

# Check if running with admin/sudo permissions
check_permissions() {
    if [ "$(id -u)" != "0" ]; then
        echo -e "${YELLOW}Warning: This script might need administrator privileges for some operations.${NC}"
        echo -e "${YELLOW}Consider running with 'sudo' if you encounter permission issues.${NC}"
        echo ""
        sleep 2
    fi
}

# Check if a command exists
command_exists() {
    command -v "$1" &> /dev/null
}

# Install .NET SDK if not found
install_dotnet() {
    echo -e "${BLUE}Checking for .NET SDK...${NC}"
    
    if command_exists dotnet; then
        DOTNET_VERSION=$(dotnet --version)
        echo -e "${GREEN}.NET SDK found (Version: $DOTNET_VERSION)${NC}"
        
        # Check if version is at least 8.0
        if [[ $DOTNET_VERSION < "8.0" ]]; then
            echo -e "${YELLOW}Warning: NCM3 requires .NET 8.0 or higher. Current version: $DOTNET_VERSION${NC}"
            echo -e "${YELLOW}Consider upgrading your .NET SDK.${NC}"
        fi
    else
        echo -e "${YELLOW}.NET SDK not found. Installing...${NC}"
        
        if command_exists apt-get; then
            # Debian/Ubuntu
            wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
            sudo dpkg -i packages-microsoft-prod.deb
            rm packages-microsoft-prod.deb
            
            sudo apt-get update
            sudo apt-get install -y dotnet-sdk-8.0
        elif command_exists dnf; then
            # Fedora/RHEL
            sudo dnf install dotnet-sdk-8.0
        elif command_exists brew; then
            # macOS
            brew install --cask dotnet-sdk
        else
            echo -e "${RED}Could not install .NET SDK automatically.${NC}"
            echo -e "${YELLOW}Please install .NET 8.0 SDK manually from: https://dotnet.microsoft.com/download${NC}"
        fi
        
        if command_exists dotnet; then
            echo -e "${GREEN}.NET SDK installed successfully.${NC}"
        else
            echo -e "${RED}Failed to install .NET SDK. Please install manually.${NC}"
            exit 1
        fi
    fi
}

# Install Entity Framework Tools
install_ef_tools() {
    echo -e "${BLUE}Installing Entity Framework Core tools...${NC}"
    dotnet tool install --global dotnet-ef || dotnet tool update --global dotnet-ef
    echo -e "${GREEN}Entity Framework Core tools installed.${NC}"
}

# Install SQL Server support packages
install_sqlserver_packages() {
    echo -e "${BLUE}Installing SQL Server packages...${NC}"
    
    if command_exists dotnet; then
        # Create a temporary project to install packages
        dotnet add NCM3/NCM3.csproj package Microsoft.EntityFrameworkCore.SqlServer
        dotnet add NCM3/NCM3.csproj package Microsoft.EntityFrameworkCore.Design
        echo -e "${GREEN}SQL Server packages installed.${NC}"
    else
        echo -e "${RED}Cannot install SQL Server packages: dotnet not found.${NC}"
    fi
}

# Install optional SNMP libraries
install_snmp_libraries() {
    echo -e "${BLUE}Installing SNMP libraries...${NC}"
    
    if command_exists apt-get; then
        sudo apt-get install -y libsnmp-dev
        echo -e "${GREEN}SNMP libraries installed.${NC}"
    elif command_exists dnf; then
        sudo dnf install net-snmp-devel
        echo -e "${GREEN}SNMP libraries installed.${NC}"
    elif command_exists brew; then
        brew install net-snmp
        echo -e "${GREEN}SNMP libraries installed.${NC}"
    else
        echo -e "${YELLOW}Could not install SNMP libraries automatically.${NC}"
        echo -e "${YELLOW}You might need to install SNMP development libraries manually.${NC}"
    fi
    
    # Add NuGet package
    dotnet add NCM3/NCM3.csproj package SnmpSharpNet
}

# Install AWS SDK
install_aws_sdk() {
    echo -e "${BLUE}Installing AWS SDK...${NC}"
    dotnet add NCM3/NCM3.csproj package AWSSDK.S3
    dotnet add NCM3/NCM3.csproj package AWSSDK.Extensions.NETCore.Setup
    echo -e "${GREEN}AWS SDK installed.${NC}"
}

# Setup configuration files
setup_configs() {
    echo -e "${BLUE}Setting up configuration files...${NC}"
    
    if [ ! -f "NCM3/appsettings.secrets.json" ]; then
        cp NCM3/appsettings.json NCM3/appsettings.secrets.json
        echo -e "${GREEN}Created appsettings.secrets.json template.${NC}"
        echo -e "${YELLOW}Please update appsettings.secrets.json with your actual credentials.${NC}"
    else
        echo -e "${GREEN}appsettings.secrets.json already exists.${NC}"
    fi
}

# Create database schema
setup_database() {
    echo -e "${BLUE}Setting up database...${NC}"
    echo -e "${YELLOW}Note: Make sure your connection string is correctly set in appsettings.secrets.json${NC}"
    
    # Prompt for confirmation
    read -p "Do you want to create/update the database now? (y/n): " choice
    if [[ "$choice" =~ ^[Yy]$ ]]; then
        dotnet ef database update --project NCM3/NCM3.csproj
        if [ $? -eq 0 ]; then
            echo -e "${GREEN}Database setup completed successfully.${NC}"
        else
            echo -e "${RED}Database setup failed. Check your connection string.${NC}"
        fi
    else
        echo -e "${YELLOW}Database setup skipped. You'll need to run 'dotnet ef database update' manually.${NC}"
    fi
}

# Restore and build project
build_project() {
    echo -e "${BLUE}Restoring and building NCM3...${NC}"
    dotnet restore
    dotnet build
    
    if [ $? -eq 0 ]; then
        echo -e "${GREEN}NCM3 built successfully.${NC}"
    else
        echo -e "${RED}Build failed. See error messages above.${NC}"
    fi
}

# Create Docker setup if Docker is available
setup_docker() {
    if command_exists docker; then
        echo -e "${BLUE}Docker detected. Setting up Docker environment...${NC}"
        
        # Check if Dockerfile exists, if not create one
        if [ ! -f "Dockerfile" ]; then
            cat > Dockerfile << 'EOL'
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["NCM3/NCM3.csproj", "NCM3/"]
RUN dotnet restore "NCM3/NCM3.csproj"
COPY . .
WORKDIR "/src/NCM3"
RUN dotnet build "NCM3.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NCM3.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NCM3.dll"]
EOL
            echo -e "${GREEN}Dockerfile created.${NC}"
        fi
        
        # Create docker-compose.yml for easier management
        if [ ! -f "docker-compose.yml" ]; then
            cat > docker-compose.yml << 'EOL'
version: '3.8'

services:
  ncm3:
    build: .
    container_name: ncm3
    ports:
      - "8080:80"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    volumes:
      - ./NCM3/appsettings.secrets.json:/app/appsettings.secrets.json
      - ./Logs:/app/Logs
      - ./ConfigBackups:/app/ConfigBackups
    restart: unless-stopped
    
  # Uncomment to add SQL Server container (optional)
  # db:
  #   image: mcr.microsoft.com/mssql/server:2019-latest
  #   container_name: ncm3-sqlserver
  #   environment:
  #     SA_PASSWORD: "YourStrongP@ssword"
  #     ACCEPT_EULA: "Y"
  #   ports:
  #     - "1433:1433"
  #   volumes:
  #     - sqlserver_data:/var/opt/mssql
  #   restart: unless-stopped

# Uncomment if using SQL Server container
# volumes:
#   sqlserver_data:
EOL
            echo -e "${GREEN}Docker Compose file created.${NC}"
        fi
        
        echo -e "${YELLOW}To build and run with Docker, use:${NC}"
        echo -e "  docker-compose build"
        echo -e "  docker-compose up -d"
    else
        echo -e "${YELLOW}Docker not found. Skipping Docker setup.${NC}"
    fi
}

# Main execution

check_permissions
install_dotnet
install_ef_tools
install_sqlserver_packages
install_snmp_libraries
install_aws_sdk
setup_configs
build_project
setup_database
setup_docker

echo -e "${BLUE}"
echo "==============================================================="
echo "                NCM3 Setup Completed                          "
echo "==============================================================="
echo -e "${NC}"
echo -e "${GREEN}You can now run the project using:${NC}"
echo -e "  dotnet run --project NCM3/NCM3.csproj"
echo ""
echo -e "${YELLOW}Remember to update appsettings.secrets.json with your credentials.${NC}"
echo ""
echo -e "${BLUE}Thank you for using NCM3!${NC}"
