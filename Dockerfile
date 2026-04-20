# 建立編譯環境
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish -c Release -o out

# 建立執行環境
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .

# 設定 Render 預設使用的 Port
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# 啟動 API (確認這裡的 dll 名稱跟你專案名稱一致)
ENTRYPOINT ["dotnet", "GraduationApi.dll"]