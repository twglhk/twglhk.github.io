#!/bin/bash

# Jekyll 서버 시작
echo "Starting Jekyll server..."
bundle exec jekyll serve &

# 서버 PID 저장
JEKYLL_PID=$!

# 서버가 시작되기를 기다림
sleep 3

# 브라우저에서 열기
echo "Opening http://127.0.0.1:4000/ in your browser..."
open http://127.0.0.1:4000/

# 서버 실행 시간을 설정 (예: 60초)
SERVER_RUNTIME=10
echo "Jekyll server will run for $SERVER_RUNTIME seconds..."

# 지정된 시간 동안 대기
sleep $SERVER_RUNTIME

# Jekyll 서버 종료
echo "Stopping Jekyll server..."
kill $JEKYLL_PID

echo "Jekyll server stopped."
