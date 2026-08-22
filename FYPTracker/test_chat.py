import urllib.request
import urllib.parse
from http.cookiejar import CookieJar
import ssl
import re

# Ignore SSL errors for localhost
ctx = ssl.create_default_context()
ctx.check_hostname = False
ctx.verify_mode = ssl.CERT_NONE

cj = CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj), urllib.request.HTTPSHandler(context=ctx))

try:
    print('Fetching Login...')
    resp = opener.open('https://localhost:44326/User/Login')
    html = resp.read().decode('utf-8')
    token_match = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', html)
    token = token_match.group(1) if token_match else ''
    
    print('Posting Login...')
    data = urllib.parse.urlencode({
        'Email': 'dr.smith@university.edu',
        'Password': 'password123',
        '__RequestVerificationToken': token
    }).encode('utf-8')
    
    req = urllib.request.Request('https://localhost:44326/User/Login', data=data)
    opener.open(req)
    
    print('Fetching Chat...')
    chat_resp = opener.open('https://localhost:44326/Chat')
    chat_html = chat_resp.read().decode('utf-8')
    
    if 'debug-tag' in chat_html:
        print('SUCCESS: DEBUG TAG FOUND IN HTML!')
    else:
        print('FAILURE: DEBUG TAG NOT FOUND! The server is serving old cached views!')
        
except Exception as e:
    print('Error:', e)
