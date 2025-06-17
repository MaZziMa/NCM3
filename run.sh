#!/bin/bash

# NCM3 Run Script for Linux
# This script provides various commands to run and manage NCM3

# Text colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Variables
PROJECT_DIR=$(dirname "$(readlink -f "$0")")
PROJECT_PATH="${PROJECT_DIR}/NCM3/NCM3.csproj"
LOG_DIR="${PROJECT_DIR}/Logs"
PID_FILE="/tmp/ncm3.pid"

# Show banner
show_banner() {
    clear
    echo -e "${BLUE}"
    echo "============================================================"
    echo "                NCM3 - Management Console                   "
    echo "============================================================"
    echo -e "${NC}"
}

# Show usage help
show_help() {
    echo -e "Usage: $0 [command]"
    echo
    echo -e "Commands:"
    echo -e "  ${GREEN}start${NC}        Start NCM3 (default)"
    echo -e "  ${GREEN}stop${NC}         Stop the running NCM3 instance"
    echo -e "  ${GREEN}restart${NC}      Restart NCM3"
    echo -e "  ${GREEN}status${NC}       Check if NCM3 is running"
    echo -e "  ${GREEN}logs${NC}         View logs"
    echo -e "  ${GREEN}test${NC}         Run tests"
    echo -e "  ${GREEN}update${NC}       Update database to latest migration"
    echo -e "  ${GREEN}docker${NC}       Run with Docker"
    echo -e "  ${GREEN}help${NC}         Show this help"
    echo
}

# Check if NCM3 is running
is_running() {
    if [ -f "$PID_FILE" ]; then
        PID=$(cat "$PID_FILE")
        if ps -p "$PID" > /dev/null; then
            return 0  # Running
        else
            rm -f "$PID_FILE"  # Clean up stale PID file
        fi
    fi
    return 1  # Not running
}

# Start NCM3
start_ncm3() {
    echo -e "${BLUE}Starting NCM3...${NC}"
    
    # Check if already running
    if is_running; then
        echo -e "${YELLOW}NCM3 is already running with PID $(cat $PID_FILE)${NC}"
        return 0
    fi
    
    # Make sure log directory exists
    mkdir -p "$LOG_DIR"
    
    # Start the application
    cd "$PROJECT_DIR" || exit
    
    # Check environment
    if [ -z "$ASPNETCORE_ENVIRONMENT" ]; then
        export ASPNETCORE_ENVIRONMENT="Production"
    fi
    
    # Start application in background and save PID
    dotnet run --project "$PROJECT_PATH" > "$LOG_DIR/ncm3-console.log" 2>&1 &
    PID=$!
    echo $PID > "$PID_FILE"
    
    echo -e "${GREEN}NCM3 started with PID $PID (Environment: $ASPNETCORE_ENVIRONMENT)${NC}"
    echo -e "Console output is being logged to $LOG_DIR/ncm3-console.log"
    
    # Give it a few seconds and check if still running
    sleep 3
    if is_running; then
        echo -e "${GREEN}NCM3 is now running on http://localhost:5000${NC}"
    else
        echo -e "${RED}NCM3 failed to start! Check logs for details.${NC}"
        cat "$LOG_DIR/ncm3-console.log"
    fi
}

# Stop NCM3
stop_ncm3() {
    echo -e "${BLUE}Stopping NCM3...${NC}"
    
    if is_running; then
        PID=$(cat "$PID_FILE")
        echo -e "Stopping NCM3 process with PID $PID"
        
        # Try graceful shutdown first
        kill -15 "$PID"
        sleep 5
        
        # Check if it's still running
        if ps -p "$PID" > /dev/null; then
            echo -e "${YELLOW}Process is still running. Forcing termination...${NC}"
            kill -9 "$PID"
        fi
        
        rm -f "$PID_FILE"
        echo -e "${GREEN}NCM3 stopped${NC}"
    else
        echo -e "${YELLOW}NCM3 is not running${NC}"
    fi
}

# Check status
check_status() {
    if is_running; then
        PID=$(cat "$PID_FILE")
        echo -e "${GREEN}NCM3 is running with PID $PID${NC}"
        
        # Get runtime info
        UPTIME=$(ps -o etime= -p "$PID")
        MEM=$(ps -o rss= -p "$PID" | awk '{print $1/1024}')
        
        echo -e "Uptime: $UPTIME"
        printf "Memory usage: %.2f MB\n" "$MEM"
        
        # Get URL
        echo -e "URL: http://localhost:5000"
        
        # Show database connection status
        if command -v dotnet-ef &> /dev/null; then
            echo -e "\nChecking database connection..."
            cd "$PROJECT_DIR" || exit
            CONNECTION_STATUS=$(dotnet ef database verify -p "$PROJECT_PATH" --no-build 2>&1)
            if [ $? -eq 0 ]; then
                echo -e "${GREEN}Database connection successful${NC}"
            else
                echo -e "${RED}Database connection failed${NC}"
                echo "$CONNECTION_STATUS"
            fi
        fi
    else
        echo -e "${RED}NCM3 is not running${NC}"
    fi
}

# Show logs
show_logs() {
    echo -e "${BLUE}Recent logs:${NC}"
    
    # Check if log file exists
    if [ -f "$LOG_DIR/ncm3-console.log" ]; then
        echo -e "${YELLOW}=== Console Log ===${NC}"
        tail -n 50 "$LOG_DIR/ncm3-console.log"
    else
        echo -e "${RED}No console log found${NC}"
    fi
    
    echo
    
    # Check for application logs
    APP_LOGS=$(find "$LOG_DIR" -name "ncm3-*.log" | sort -r | head -n 1)
    if [ -n "$APP_LOGS" ]; then
        echo -e "${YELLOW}=== Application Log ===${NC}"
        tail -n 50 "$APP_LOGS"
    fi
    
    echo
    echo -e "Complete logs are available in: $LOG_DIR"
}

# Run tests
run_tests() {
    echo -e "${BLUE}Running tests...${NC}"
    cd "$PROJECT_DIR" || exit
    dotnet test Tests/NCM3.Tests.csproj -v n
    
    if [ $? -eq 0 ]; then
        echo -e "${GREEN}All tests passed!${NC}"
    else
        echo -e "${RED}Some tests failed. Check the output above for details.${NC}"
    fi
}

# Update database
update_database() {
    echo -e "${BLUE}Updating database...${NC}"
    cd "$PROJECT_DIR" || exit
    
    dotnet ef database update -p "$PROJECT_PATH"
    
    if [ $? -eq 0 ]; then
        echo -e "${GREEN}Database updated successfully${NC}"
    else
        echo -e "${RED}Database update failed. See errors above.${NC}"
    fi
}

# Run with docker
run_docker() {
    echo -e "${BLUE}Managing Docker containers...${NC}"
    
    # Check if docker is installed
    if ! command -v docker &> /dev/null; then
        echo -e "${RED}Docker is not installed!${NC}"
        exit 1
    fi
    
    # Check if docker-compose.yml exists
    if [ ! -f "$PROJECT_DIR/docker-compose.yml" ]; then
        echo -e "${RED}docker-compose.yml not found!${NC}"
        echo -e "${YELLOW}Run './setup.sh' first to create Docker files${NC}"
        exit 1
    fi
    
    # Docker submenu
    echo -e "Select Docker operation:"
    echo -e "  ${GREEN}1${NC}) Start containers"
    echo -e "  ${GREEN}2${NC}) Stop containers"
    echo -e "  ${GREEN}3${NC}) View logs"
    echo -e "  ${GREEN}4${NC}) Rebuild and restart"
    echo -e "  ${GREEN}0${NC}) Back"
    
    read -p "Enter your choice: " DOCKER_CHOICE
    
    case $DOCKER_CHOICE in
        1)
            echo -e "Starting Docker containers..."
            docker-compose -f "$PROJECT_DIR/docker-compose.yml" up -d
            ;;
        2)
            echo -e "Stopping Docker containers..."
            docker-compose -f "$PROJECT_DIR/docker-compose.yml" down
            ;;
        3)
            echo -e "Showing Docker logs (press Ctrl+C to exit)..."
            docker-compose -f "$PROJECT_DIR/docker-compose.yml" logs -f
            ;;
        4)
            echo -e "Rebuilding and restarting..."
            docker-compose -f "$PROJECT_DIR/docker-compose.yml" build
            docker-compose -f "$PROJECT_DIR/docker-compose.yml" up -d
            ;;
        0)
            return
            ;;
        *)
            echo -e "${RED}Invalid option${NC}"
            ;;
    esac
}

# Main function
main() {
    show_banner
    
    COMMAND=${1:-start}
    
    case $COMMAND in
        "start")
            start_ncm3
            ;;
        "stop")
            stop_ncm3
            ;;
        "restart")
            stop_ncm3
            sleep 2
            start_ncm3
            ;;
        "status")
            check_status
            ;;
        "logs")
            show_logs
            ;;
        "test")
            run_tests
            ;;
        "update")
            update_database
            ;;
        "docker")
            run_docker
            ;;
        "help"|"--help"|"-h")
            show_help
            ;;
        *)
            echo -e "${RED}Unknown command: $COMMAND${NC}"
            show_help
            exit 1
            ;;
    esac
}

# Execute main function
main "$@"
