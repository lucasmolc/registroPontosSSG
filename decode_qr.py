"""Script para decodificar QR code de 2FA e extrair a secret key."""
import sys
from PIL import Image
from pyzbar.pyzbar import decode
from urllib.parse import urlparse, parse_qs

def decode_qr(image_path: str) -> dict:
    """Decodifica QR code e extrai informações TOTP."""
    img = Image.open(image_path)
    decoded = decode(img)
    
    if not decoded:
        print("Erro: Não foi possível decodificar o QR code")
        return None
    
    data = decoded[0].data.decode('utf-8')
    print(f"Conteúdo do QR: {data}")
    
    # Parse otpauth URL
    parsed = urlparse(data)
    params = parse_qs(parsed.query)
    
    result = {
        'type': parsed.scheme,
        'label': parsed.path[1:] if parsed.path else '',
        'secret': params.get('secret', [''])[0],
        'issuer': params.get('issuer', [''])[0],
        'algorithm': params.get('algorithm', ['SHA1'])[0],
        'digits': params.get('digits', ['6'])[0],
        'period': params.get('period', ['30'])[0],
    }
    
    return result

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Uso: python decode_qr.py <caminho_imagem>")
        sys.exit(1)
    
    info = decode_qr(sys.argv[1])
    if info:
        print("\n=== Informações extraídas ===")
        print(f"Secret Key: {info['secret']}")
        print(f"Issuer: {info['issuer']}")
        print(f"Label: {info['label']}")
        print(f"Algoritmo: {info['algorithm']}")
        print(f"Dígitos: {info['digits']}")
        print(f"Período: {info['period']}s")
