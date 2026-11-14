# datos/

`catalogo_a.json` es el archivo de trabajo del orquestador de A (lista de registros del Contrato 2).
Lo escribe la actualización automática y se guarda en el repositorio para que, en la corrida siguiente,
A conserve el último dato bueno de cada fuente que falle. **No editar a mano.**
La página no lee este archivo: lee `web/data/catalogo.json`, que es la copia validada por `scripts/publicar_catalogo.py`.
