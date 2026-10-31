# Nodalis v1.9 — Comparateur original / Markdown et dry-run (#162, premier incrément)

L'onglet **Comparateur** de l'assistant DOCX affiche côte à côte le **texte Word interprété**, y compris les blocs exclus, et le **Markdown actuellement projeté**, tenant compte des sélections, réaffectations et corrections de titres. Une liste des sections Word permet de choisir le bloc de comparaison ; sa ligne de statut indique si la section est ignorée, sa destination Nodalis ou le nombre de blocs retenus.

Le comparateur expose les valeurs textuelles des tableaux, les alt-text des images et les alertes des objets complexes. Ce n'est pas un rendu Word pixel-perfect, et l'original DOCX reste conservé.

L'onglet **Fichiers** garde la liste des fichiers réellement prévus, y compris les images sélectionnées et réutilisées, et les avertissements. L'utilisateur doit d'abord calculer puis confirmer explicitement le plan. Des incohérences de règles de profil bloquent la confirmation. La preview n'écrit rien.

Limites restantes : diff à la ligne, navigation synchronisée avec l'arbre de sections, détails par fichier cible / pièce jointe, gestion de versions concurrentes entre plan et commit, annulation d'un calcul volumineux, recette WPF DPI/performances. L'issue #162 ne peut pas être fermée à ce stade.

## Protection contre la dérive entre aperçu et import

Le plan comprend maintenant l'empreinte SHA-256 de ses opérations **dans leur ordre**, des chemins destination, des avertissements et du contenu Markdown réellement projeté. Lorsque l'utilisateur clique sur Valider, cette empreinte est jointe à sa requête. Juste avant toute mutation du workspace, le service recalcule le plan ; un changement externe de nom de fichier, de destination ou du contenu **bloque l'import avec un message explicite** plutôt que de produire une destination surprise.

Cette vérification initiale ne constitue **pas** une transaction interprocessus : une modification concurrente survenant *après* le recalcul mais avant les écritures demeure possible. La prévention de cette fenêtre et la qualification sous forte concurrence restent ouvertes dans #162.

## Chemins gelés entre prévisualisation et commit

L'écriture utilise désormais **les chemins exacts du plan recalculé et validé au démarrage du commit**, pour chaque Markdown et la copie DOCX dans `Imports/Sources`. Elle ne rappelle pas la recherche de noms alternatifs en cours de transaction. Une collision créée par une autre application après approbation provoque un refus sans renommage silencieux ; le rollback conserve les fichiers tiers. Les tests `DocxProvenanceSmokeTests` injectent ces deux collisions à des checkpoints différents. Cela améliore la concurrence contrôlée, mais **ne remplace pas** le journal durable de récupération en cas d'arrêt brutal (#165).

## Précision des fichiers initiaux (#162)

Les plans de nouveaux projets distinguent maintenant les profils : **Ultra-simple** annonce `Fiche de Maintenance.md` au lieu de `Présentation.md`, et les sections singleton **Jalons** / **Glossaire** sont projetées comme documents à la racine, identiques à la création réelle. Une vérification automatisée compare les fichiers prévus et les fichiers créés pour les profils Ultra-simple et Moyen.
