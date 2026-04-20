# 建立編譯環境 (這裡改成 10.0)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish -c Release -o out

# 建立執行環境 (這裡也改成 10.0)
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .

# 設定 Render 預設使用的 Port
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# 啟動 API
ENTRYPOINT ["dotnet", "GraduationApi.dll"]