pipeline {
    agent any

    options {
        disableConcurrentBuilds()
        timestamps()
        timeout(time: 45, unit: 'MINUTES')
        buildDiscarder(logRotator(numToKeepStr: '10'))
    }

    triggers {
        pollSCM('H/5 * * * *')
    }

    environment {
        DOCKERHUB_NAMESPACE = 'sneha348'
        K8S_NAMESPACE = 'fundoo-microservice'
        KUBECONFIG = '/var/lib/jenkins/.kube/fundoo-config'
    }

    stages {
        stage('Build and push images') {
            steps {
                script {
                    env.IMAGE_TAG = sh(
                        script: 'git rev-parse --short=12 HEAD',
                        returnStdout: true
                    ).trim()
                }

                withCredentials([usernamePassword(
                    credentialsId: 'dockerhub-fundoo',
                    usernameVariable: 'DOCKERHUB_USERNAME',
                    passwordVariable: 'DOCKERHUB_TOKEN'
                )]) {
                    sh '''#!/bin/sh
                        set -eu
                        printf '%s' "$DOCKERHUB_TOKEN" | docker login --username "$DOCKERHUB_USERNAME" --password-stdin

                        docker build --build-arg PROJECT_PATH=src/Services/User/Fundoo.User.Api/Fundoo.User.Api.csproj --build-arg DLL_NAME=Fundoo.User.Api.dll -t "$DOCKERHUB_NAMESPACE/fundoo-user-api:$IMAGE_TAG" .
                        docker build --build-arg PROJECT_PATH=src/Services/Notes/Fundoo.Notes.Api/Fundoo.Notes.Api.csproj --build-arg DLL_NAME=Fundoo.Notes.Api.dll -t "$DOCKERHUB_NAMESPACE/fundoo-notes-api:$IMAGE_TAG" .
                        docker build --build-arg PROJECT_PATH=src/Services/Notification/Fundoo.Notification.Api/Fundoo.Notification.Api.csproj --build-arg DLL_NAME=Fundoo.Notification.Api.dll -t "$DOCKERHUB_NAMESPACE/fundoo-notification-api:$IMAGE_TAG" .
                        docker build --build-arg PROJECT_PATH=src/ApiGateway/Fundoo.ApiGateway/Fundoo.ApiGateway.csproj --build-arg DLL_NAME=Fundoo.ApiGateway.dll -t "$DOCKERHUB_NAMESPACE/fundoo-api-gateway:$IMAGE_TAG" .

                        docker push "$DOCKERHUB_NAMESPACE/fundoo-user-api:$IMAGE_TAG"
                        docker push "$DOCKERHUB_NAMESPACE/fundoo-notes-api:$IMAGE_TAG"
                        docker push "$DOCKERHUB_NAMESPACE/fundoo-notification-api:$IMAGE_TAG"
                        docker push "$DOCKERHUB_NAMESPACE/fundoo-api-gateway:$IMAGE_TAG"
                    '''
                }
            }
            post {
                always {
                    sh 'docker logout >/dev/null 2>&1 || true'
                }
            }
        }

        stage('Deploy to Kubernetes') {
            steps {
                sh '''#!/bin/sh
                    set -eu
                    kubectl -n "$K8S_NAMESPACE" set image deployment/user-api user-api="$DOCKERHUB_NAMESPACE/fundoo-user-api:$IMAGE_TAG"
                    kubectl -n "$K8S_NAMESPACE" set image deployment/notes-api notes-api="$DOCKERHUB_NAMESPACE/fundoo-notes-api:$IMAGE_TAG"
                    kubectl -n "$K8S_NAMESPACE" set image deployment/notification-api notification-api="$DOCKERHUB_NAMESPACE/fundoo-notification-api:$IMAGE_TAG"
                    kubectl -n "$K8S_NAMESPACE" set image deployment/api-gateway api-gateway="$DOCKERHUB_NAMESPACE/fundoo-api-gateway:$IMAGE_TAG"

                    kubectl -n "$K8S_NAMESPACE" rollout status deployment/user-api --timeout=180s
                    kubectl -n "$K8S_NAMESPACE" rollout status deployment/notes-api --timeout=180s
                    kubectl -n "$K8S_NAMESPACE" rollout status deployment/notification-api --timeout=180s
                    kubectl -n "$K8S_NAMESPACE" rollout status deployment/api-gateway --timeout=180s
                '''
            }
        }
    }
}
