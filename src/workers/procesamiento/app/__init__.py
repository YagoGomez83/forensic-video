import os

# Antes de importar cv2: tope de píxeles por imagen contra bombas de descompresión (100 MP).
os.environ.setdefault("OPENCV_IO_MAX_IMAGE_PIXELS", str(100_000_000))
