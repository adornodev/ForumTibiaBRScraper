@echo off
C:
cd C:\Users\USUARIO\Documents\ForumTibiaBRScraper\ForumTibiaBR

for /l %%x in (1, 1, 5) do (
    start "ForumTibiaBr - CommentsParser" /MIN "C:\Users\USUARIO\Documents\ForumTibiaBRScraper\ForumTibiaBR\CommentsParser\bin\Release\CommentsParser.exe"
	timeout 5
)
exit