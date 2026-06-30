## Login
https://chutes.ai/api/auth/login

request body : application/json  
{fingerprint: "YOUR_FINGERPRINT", returnTo: "/"}

result : set-cookie only

## balances
https://chutes.ai/api/dashboard/balance

need cookie

response body : application/json

{
    "balance": {
        "usd": 4.851758750000001,
        "tao": 0
    },
    "payment_address": "?",
    "plan": {
        "tier": "Free",
        "name": "Chutes Flex",
        "requests_per_day": 0,
        "price": "Free"
    }
}

