#!/bin/bash
set -e

# Default values
TARGET="${1:-all}"
CONFIGURATION="${2:-Release}"

# Colors
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
GREEN='\033[0;32m'
RED='\033[0;31m'
GRAY='\033[0;37m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

build_client_js() {
    echo -e "${CYAN}🔨 Building client-js (TypeScript → JavaScript)...${NC}"
    
    cd client-js
    
    # Check if node_modules exists
    if [ ! -d "node_modules" ]; then
        echo -e "${YELLOW}📦 Installing npm dependencies...${NC}"
        npm install --ignore-scripts
    fi
    
    echo -e "${YELLOW}🏗️  Building with Vite...${NC}"
    npm run build
    
    cd ..
    
    echo -e "${GREEN}✅ client-js build complete!${NC}"
    echo -e "${GRAY}   Output: src/wwwroot/js/servicebus-api.js${NC}"
}

build_dotnet() {
    echo -e "${CYAN}🔨 Building .NET Blazor WebAssembly...${NC}"
    
    echo -e "${YELLOW}🏗️  Running dotnet publish...${NC}"
    dotnet publish src/ServiceBusExplorer.Blazor.csproj -c "$CONFIGURATION"
    
    echo -e "${GREEN}✅ .NET build complete!${NC}"
    echo -e "${GRAY}   Output: src/bin/$CONFIGURATION/net10.0/publish/wwwroot${NC}"
}

# Main build logic
START_TIME=$(date +%s)

echo -e "${MAGENTA}🚀 Building bussin${NC}"
echo -e "${GRAY}   Target: $TARGET${NC}"
echo -e "${GRAY}   Configuration: $CONFIGURATION${NC}"
echo ""

case "$TARGET" in
    js)
        build_client_js
        ;;
    dotnet)
        build_dotnet
        ;;
    all)
        build_client_js
        echo ""
        build_dotnet
        ;;
    *)
        echo -e "${RED}❌ Invalid target: $TARGET${NC}"
        echo "Usage: $0 [js|dotnet|all] [Debug|Release]"
        exit 1
        ;;
esac

END_TIME=$(date +%s)
ELAPSED=$((END_TIME - START_TIME))

echo ""
echo -e "${GREEN}🎉 Build completed successfully in ${ELAPSED}s${NC}"
