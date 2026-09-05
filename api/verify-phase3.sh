#!/usr/bin/env bash
# Phase 3 verification: availability, double-book 409, lookup, cancel.
set -e
API=http://localhost:8080
SLUG=readysetsiivous
SERVICE_ID=1f75fb1d-9a98-43b5-bcd1-328224b0905d   # office-cleaning, 90 min
DATE=2026-09-14

echo "== availability"
curl -s "$API/api/v1/public/$SLUG/availability?date=$DATE&serviceId=$SERVICE_ID" \
  | python -c "import sys,json;d=json.load(sys.stdin);print('  slots:',len(d),'first:',d[0] if d else None)"

echo "== book 10:00"
BOOK=$(curl -s -X POST "$API/api/v1/public/$SLUG/bookings" \
  -H "Content-Type: application/json" \
  -d "{\"localDate\":\"$DATE\",\"startTime\":\"10:00\",\"serviceId\":\"$SERVICE_ID\",\"customerName\":\"Testi Asiakas\",\"customerPhone\":\"0401234567\",\"customerEmail\":\"testi@example.com\",\"street\":\"Testikatu 2\",\"postalCode\":\"00100\",\"city\":\"Helsinki\",\"notes\":null}")
echo "  $BOOK" | python -c "import sys,json;d=json.load(sys.stdin);print('  number:',d['bookingNumber'],'ref:',d['customerReference'])"
NUMBER=$(echo "$BOOK" | python -c "import sys,json;print(json.load(sys.stdin)['bookingNumber'])")

echo "== double-book 10:30 (expect 409)"
curl -s -o /dev/null -w "  HTTP %{http_code}\n" -X POST "$API/api/v1/public/$SLUG/bookings" \
  -H "Content-Type: application/json" \
  -d "{\"localDate\":\"$DATE\",\"startTime\":\"10:30\",\"serviceId\":\"$SERVICE_ID\",\"customerName\":\"Toinen Asiakas\",\"customerPhone\":\"0509876543\",\"customerEmail\":\"toinen@example.com\",\"street\":\"Katu 3\",\"postalCode\":\"00200\",\"city\":\"Helsinki\",\"notes\":null}"

echo "== availability after booking (10:00 slot gone)"
curl -s "$API/api/v1/public/$SLUG/availability?date=$DATE&serviceId=$SERVICE_ID" \
  | python -c "import sys,json;d=json.load(sys.stdin);print('  10:00 present:',any(s['startTime']=='10:00' for s in d))"

echo "== lookup"
curl -s "$API/api/v1/public/$SLUG/bookings/$NUMBER?phone=0401234567" \
  | python -c "import sys,json;d=json.load(sys.stdin);print('  status:',d['status'],'date:',d['startLocalDate'],d['startLocalTime'])"

echo "== wrong phone lookup (expect 404)"
curl -s -o /dev/null -w "  HTTP %{http_code}\n" "$API/api/v1/public/$SLUG/bookings/$NUMBER?phone=000"

echo "== cancel"
curl -s -o /dev/null -w "  HTTP %{http_code}\n" -X POST "$API/api/v1/public/$SLUG/bookings/$NUMBER/cancel?phone=0401234567"

echo "== lookup after cancel"
curl -s "$API/api/v1/public/$SLUG/bookings/$NUMBER?phone=0401234567" \
  | python -c "import sys,json;d=json.load(sys.stdin);print('  status:',d['status'])"

echo "== availability restored"
curl -s "$API/api/v1/public/$SLUG/availability?date=$DATE&serviceId=$SERVICE_ID" \
  | python -c "import sys,json;d=json.load(sys.stdin);print('  10:00 back:',any(s['startTime']=='10:00' for s in d))"

echo "== DONE"
