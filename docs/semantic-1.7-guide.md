# Nodalis 1.7 (develop) — Rechercher par le sens

**Aperçu à qualifier**, différent de la recherche exacte/fuzzy 1.0, qui reste disponible sans aucune IA.

## Préparer le fournisseur

1. Avec Ollama déjà installé localement, installer volontairement un **modèle d'embeddings** comme `embeddinggemma` : `ollama pull embeddinggemma`. Aucun modèle n'est téléchargé automatiquement par Nodalis.
2. Ouvrir **Ctrl+P → Configurer la recherche sémantique locale**. Cliquer sur **Configurer Ollama local** pour remplir le chemin du pont livré dans l'archive, `embeddinggemma` et `768` dimensions. Cocher séparément l'activation explicite, puis enregistrer. Cette étape ne transmet aucun document ni ne lance de processus. Pour un autre modèle ou un fournisseur CLI autonome, saisir ses véritables paramètres conformément au [contrat](semantic-1.7-provider.md).
3. Ouvrir **Ctrl+P → Rechercher par le sens (local)**. Saisir un chemin relatif de dossier si seul un projet doit être indexé, puis sélectionner **Indexer ce périmètre**. Nodalis affiche le nombre de documents et demande une deuxième confirmation avant tout envoi de texte au moteur local.
4. Après indexation, entrer une requête en langage naturel et lancer **Rechercher**. Les résultats affichent le titre de section éventuel, un extrait actuel, le fichier, le numéro de ligne et une **similarité cosinus** (pas une probabilité de vérité). Double-cliquer pour ouvrir le document source au bon endroit.
5. **Annuler** arrête l'opération en cours ; les fichiers déjà indexés sont conservés. **Ctrl+P → Effacer le cache sémantique** efface tous les embeddings dérivés du workspace courant, sans modifier les notes.

## Ce qui est ou non indexé

- Uniquement les fichiers `.md` canoniques répertoriés par Nodalis, non les copies virtuelles produites par `![[...]]`.
- Les métadonnées front matter, le code fenced et les blocs inclus ne sont pas envoyés au modèle par défaut.
- Les dossiers cachés, fichiers techniques, chemins en dehors du workspace et chemins sensibles habituels sont exclus par défaut.
- Les morceaux sont bornés (taille par défaut 1 200 caractères avec recouvrement 120 caractères). Le cache ne contient **pas** leur texte complet : seuls GUID, empreinte de contenu, chemin/ligne/section et vecteurs sont stockés.
- Le cache est stocké dans `%LOCALAPPDATA%/Nodalis/SemanticIndexes`, avec une identité locale dérivée du chemin du workspace. Le modèle, la dimension et la politique de découpage déterminent la compatibilité. Un changement rend les anciens vecteurs inutilisables.
- La recherche relit la source Markdown et vérifie l'identifiant du fragment avant d'afficher un résultat ; après modification externe non réindexée, les résultats périmés sont écartés.

## Confidentialité et limites

Nodalis ne fait pas de requêtes HTTP dans son cœur applicatif. Le **pont Ollama facultatif** effectue uniquement des appels locaux à `127.0.0.1:11434`, sans proxy ni redirection. Un autre programme CLI choisi manuellement pourrait avoir des capacités réseau propres : vérifier sa provenance et ses garanties. Aucun index ni processus n'est créé au démarrage sans consentement. Le fonctionnement réel est impossible en l'absence de programme compatible.

Le score cosinus décrit la proximité géométrique des embeddings. Il ne prouve ni la justesse ni un lien de causalité. Sans modèle disponible ou avec index vide, revenir à la recherche exacte/fuzzy. Les résultats sont actuellement plafonnés aux 20 meilleurs documents, avec un fragment par document.

## Évolutions et qualification

Les issues [#142 à #146](https://github.com/TristanStark/Nodalis/issues/142) couvrent le premier lot ; [#147](https://github.com/TristanStark/Nodalis/issues/147) comprend l'évaluation de pertinence sur corpus français, les tests de volume et les vérifications réelles clavier/dark mode/DPI 100–150 %. La 1.7 ne doit être taguée qu'après validation de sortie.

